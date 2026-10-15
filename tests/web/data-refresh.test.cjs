const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/wwwroot/js/app.js'), 'utf8');

function createPage({ failedStarts = 0, signalR = true } = {}) {
    let now = 0;
    let sequence = 0;
    let classChanged = () => {};
    const timers = new Map();
    const events = new Map();
    const classes = new Set();
    const refreshes = [];
    const requests = [];
    const dot = { className: 'status-dot offline' };
    const label = { textContent: 'Офлайн' };
    const pill = {
        className: 'connection-pill offline', title: 'Офлайн', attributes: {},
        setAttribute(name, value) { this.attributes[name] = value; },
        querySelector: () => dot
    };
    const schedule = (callback, delay, interval = false) => {
        const id = ++sequence;
        timers.set(id, { callback, due: now + delay, interval: interval ? delay : 0 });
        return id;
    };
    const document = {
        visibilityState: 'visible',
        activeElement: { matches: () => false },
        body: { classList: {
            contains: value => classes.has(value),
            add: value => { classes.add(value); classChanged(); },
            remove: value => { classes.delete(value); classChanged(); }
        } },
        querySelector: () => null,
        querySelectorAll: () => [],
        getElementById: id => ({ 'live-pill': pill, 'live-label': label })[id] || null,
        addEventListener(name, callback) {
            if (!events.has(name)) events.set(name, []);
            events.get(name).push(callback);
        },
        dispatchEvent(event) {
            (events.get(event.type) || []).forEach(callback => callback(event));
        }
    };
    const hub = {
        handlers: {}, state: 'Disconnected', starts: 0,
        on(name, handler) { this.handlers[name] = handler; },
        onreconnecting(handler) { this.reconnecting = handler; },
        onreconnected(handler) { this.reconnected = handler; },
        onclose(handler) { this.closed = handler; },
        async start() {
            this.starts++;
            if (this.starts <= failedStarts) throw new Error('Unavailable');
            this.state = 'Connected';
        }
    };
    class HubConnectionBuilder {
        withUrl() { return this; }
        withAutomaticReconnect() { return this; }
        configureLogging() { return this; }
        build() { return hub; }
    }
    const context = vm.createContext({
        document, console, URL, Response,
        CustomEvent: class { constructor(type, options = {}) { this.type = type; this.detail = options.detail; } },
        MutationObserver: class { constructor(callback) { classChanged = callback; } observe() {} },
        localStorage: { getItem: () => null },
        location: { pathname: '/Cameras', search: '', href: 'http://localhost/Cameras' },
        history: { replaceState() {} },
        matchMedia: () => ({ matches: false, addEventListener() {} }),
        performance: { now: () => now },
        setTimeout: (callback, delay) => schedule(callback, delay),
        setInterval: (callback, delay) => schedule(callback, delay, true),
        clearTimeout: id => timers.delete(id),
        fetch: async url => {
            requests.push(url);
            return new Response('{}', { headers: { 'Content-Type': 'application/json' } });
        },
        signalR: signalR ? { HubConnectionBuilder, HubConnectionState: { Disconnected: 'Disconnected' }, LogLevel: { Warning: 3 } } : null
    });
    context.window = context;
    document.addEventListener('camera-maintainer:data-refresh', event => refreshes.push(event.detail));
    vm.runInContext(source, context, { filename: 'app.js' });
    const settle = async () => { for (let i = 0; i < 30; i++) await Promise.resolve(); };
    const advance = async duration => {
        await settle();
        const target = now + duration;
        while (true) {
            const next = [...timers.entries()].filter(([, timer]) => timer.due <= target)
                .sort((a, b) => a[1].due - b[1].due)[0];
            if (!next) break;
            const [id, timer] = next;
            now = timer.due;
            if (timer.interval) timer.due += timer.interval;
            else timers.delete(id);
            timer.callback();
            await settle();
        }
        now = target;
        await settle();
    };
    document.dispatchEvent({ type: 'DOMContentLoaded' });
    return { context, document, hub, refreshes, requests, advance, settle, pill, label, dot };
}

test('account status follows connection, reconnect and disconnect without ONVIF text', async () => {
    const page = createPage({ failedStarts: 1 });
    await page.settle();
    assert.equal(page.label.textContent, 'Офлайн');
    assert.equal(page.dot.className, 'status-dot offline');
    await page.advance(5000);
    assert.equal(page.label.textContent, 'Онлайн');
    assert.equal(page.pill.className, 'connection-pill online');
    assert.equal(page.pill.attributes['aria-label'], 'Онлайн');
    assert.equal(page.dot.className, 'status-dot online');
    page.hub.reconnecting();
    assert.equal(page.label.textContent, 'Офлайн');
    assert.equal(page.dot.className, 'status-dot offline');
    page.hub.reconnected();
    assert.equal(page.label.textContent, 'Онлайн');
    page.hub.closed();
    assert.equal(page.label.textContent, 'Офлайн');
    assert.equal(page.pill.title, 'Офлайн');
    const unavailable = createPage({ signalR: false });
    await unavailable.settle();
    assert.equal(unavailable.label.textContent, 'Офлайн');
});

test('SignalR is primary, with no 10/15-second polling and a 60-second safety refresh', async () => {
    const page = createPage();
    await page.advance(500);
    assert.equal(page.refreshes.length, 1);
    await page.advance(59000);
    assert.equal(page.refreshes.length, 1);
    await page.advance(1000);
    assert.equal(page.refreshes.length, 2);
    assert.equal(page.refreshes[1].reason, 'fallback');
});

test('camera case details retain each fault and its independent recovery dates', () => {
    const page = createPage();
    page.context.escapeHtml = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
    const html = page.context.renderCameraCaseIssues([
        { alertType: 'LensObstructed', category: 'Physical', title: 'Lens', startedAt: '2026-09-17T07:00:00Z', recoveredAt: '2026-09-17T08:00:00Z', description: '<img src=x onerror=alert(1)>' },
        { alertType: 0, category: 0, title: 'Offline', startedAt: '2026-09-17T09:00:00Z' }
    ]);
    assert.equal((html.match(/<li /g) || []).length, 2);
    assert.equal((html.match(/<dt>Начало:<\/dt>/g) || []).length, 2);
    assert.equal((html.match(/<dt>Конец:<\/dt>/g) || []).length, 2);
    assert.equal((html.match(/<dt>Исправил:<\/dt>/g) || []).length, 2);
    assert.match(html, /12:00:00/);
    assert.match(html, /13:00:00/);
    assert.match(html, /14:00:00/);
    assert.match(html, /class="recovered"/);
    assert.match(html, /&lt;img/);
    assert.doesNotMatch(html, /<img/);
    assert.equal(page.context.renderCameraCaseIssues([]), '');
});

test('camera event bursts are merged and do not refresh alerts or unrelated cameras', async () => {
    const page = createPage();
    await page.advance(500);
    page.hub.handlers['health.updated']({ cameraId: 35 });
    page.hub.handlers['health.updated']({ cameraId: 32 });
    page.hub.handlers['camera.status_changed']({ cameraId: 35 });
    await page.advance(499);
    assert.equal(page.refreshes.length, 1);
    await page.advance(1);
    assert.equal(page.refreshes.length, 2);
    assert.deepEqual(Array.from(page.refreshes[1].topics), ['cameras']);
    assert.deepEqual(Array.from(page.refreshes[1].cameraIds), [35, 32]);
});

test('alert and notification events use their own refresh scopes', async () => {
    const page = createPage();
    await page.advance(500);
    page.hub.handlers['alerts.changed']({});
    await page.advance(500);
    assert.deepEqual(Array.from(page.refreshes.at(-1).topics), ['alerts', 'notifications', 'navigation']);
    page.hub.handlers['notification.changed']({});
    await page.advance(500);
    assert.deepEqual(Array.from(page.refreshes.at(-1).topics), ['notifications']);
});

test('an open dialog does not freeze board, notification or navigation updates', async () => {
    const page = createPage();
    await page.advance(500);
    page.document.body.classList.add('modal-open');
    page.hub.handlers['alerts.changed']({});
    await page.advance(500);
    assert.deepEqual(Array.from(page.refreshes.at(-1).topics), ['alerts', 'notifications', 'navigation']);
    page.document.body.classList.remove('modal-open');
    await page.advance(500);
    assert.equal(page.refreshes.length, 2);
});

test('in-flight page updates finish before the next merged update', async () => {
    const page = createPage();
    await page.advance(500);
    let release;
    const work = new Promise(resolve => { release = resolve; });
    page.document.addEventListener('camera-maintainer:data-refresh', event => event.detail.waitUntil(work));
    page.hub.handlers['health.updated']({ cameraId: 1 });
    await page.advance(500);
    page.hub.handlers['health.updated']({ cameraId: 2 });
    page.hub.handlers['health.updated']({ cameraId: 3 });
    await page.advance(2000);
    assert.equal(page.refreshes.length, 2);
    release();
    await page.advance(500);
    assert.equal(page.refreshes.length, 3);
    assert.deepEqual(Array.from(page.refreshes.at(-1).cameraIds), [2, 3]);
});

for (const className of ['alert-dragging', 'alert-moving']) {
    test('pending updates resume after ' + className, async () => {
        const page = createPage();
        await page.advance(500);
        page.document.body.classList.add(className);
        page.hub.handlers['health.updated']({ cameraId: 7 });
        await page.advance(1000);
        assert.equal(page.refreshes.length, 1);
        page.document.body.classList.remove(className);
        await page.advance(500);
        assert.equal(page.refreshes.length, 2);
        assert.deepEqual(Array.from(page.refreshes.at(-1).cameraIds), [7]);
    });
}

test('focused inputs do not freeze unrelated camera data', async () => {
    const page = createPage();
    await page.advance(500);
    page.document.activeElement.matches = () => true;
    page.hub.handlers['health.updated']({ cameraId: 8 });
    await page.advance(1000);
    assert.equal(page.refreshes.length, 2);
    page.document.dispatchEvent({ type: 'focusout' });
    page.document.activeElement.matches = () => false;
    await page.advance(500);
    assert.equal(page.refreshes.length, 2);
});

test('workspace events refresh column configuration, accounts, settings and camera membership', async () => {
    const page = createPage();
    await page.advance(500);
    page.hub.handlers['workspace.changed']({ topics: ['alerts', 'users', 'settings', 'registry', 'queue', 'access'] });
    await page.advance(500);
    assert.deepEqual(Array.from(page.refreshes.at(-1).topics), ['alerts', 'users', 'settings', 'registry', 'queue', 'access']);
});

test('permission revocation is processed even during dragging, before page data', async () => {
    const page = createPage();
    await page.advance(500);
    let checks = 0;
    page.context.refreshWorkspaceAccess = async () => { checks++; return false; };
    page.document.body.classList.add('alert-dragging');
    page.hub.handlers['workspace.changed']({ topics: ['access', 'alerts'] });
    await page.advance(500);
    assert.equal(checks, 1);
    assert.equal(page.refreshes.length, 1);
});

test('hidden tabs defer requests and resynchronize on return', async () => {
    const page = createPage();
    await page.advance(500);
    page.document.visibilityState = 'hidden';
    page.hub.handlers['health.updated']({ cameraId: 8 });
    await page.advance(120000);
    assert.equal(page.refreshes.length, 1);
    page.document.visibilityState = 'visible';
    page.document.dispatchEvent({ type: 'visibilitychange' });
    await page.advance(500);
    assert.equal(page.refreshes.length, 2);
    assert.equal(page.refreshes.at(-1).cameraIds, null);
});

test('failed initial connection is retried, and reconnect resynchronizes all data', async () => {
    const page = createPage({ failedStarts: 1 });
    await page.advance(5000);
    assert.equal(page.hub.starts, 2);
    await page.advance(500);
    assert.equal(page.refreshes.at(-1).reason, 'connected');
    page.hub.reconnected();
    await page.advance(500);
    assert.equal(page.refreshes.at(-1).cameraIds, null);
    page.hub.state = 'Disconnected';
    page.hub.closed();
    await page.advance(5000);
    assert.equal(page.hub.starts, 3);
});

test('fallback works when SignalR is unavailable', async () => {
    const page = createPage({ signalR: false });
    await page.advance(60500);
    assert.equal(page.refreshes.length, 2);
    assert.equal(page.refreshes[1].reason, 'fallback');
});

test('concurrent identical GETs share a request, without caching subsequent reads or POSTs', async () => {
    const page = createPage();
    await page.advance(500);
    const url = '/api/cameras/35';
    await Promise.all([page.context.apiFetch(url), page.context.apiFetch(url)]);
    assert.equal(page.requests.filter(item => item === url).length, 1);
    await page.context.apiFetch(url);
    assert.equal(page.requests.filter(item => item === url).length, 2);
    await Promise.all([page.context.apiFetch(url, { method: 'POST' }), page.context.apiFetch(url, { method: 'POST' })]);
    assert.equal(page.requests.filter(item => item === url).length, 4);
});
