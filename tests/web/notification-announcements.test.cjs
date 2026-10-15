const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/wwwroot/js/app.js'), 'utf8');
const source = app.slice(app.indexOf('    let accountNotificationItems = []'), app.indexOf('    function renderAccountNotificationList()'));

function page({ soundEnabled = true, popupEnabled = true } = {}) {
    const elements = new Map();
    const listeners = new Map();
    let starts = 0, timedCallbacks = 0;
    const feed = { count: 1, soundEnabled, popupEnabled, items: [{
        notificationId: 'n:7', alertId: 7, isRead: false, title: 'New task', message: 'Camera 7: check power',
        url: '/Alerts?view=active&alert=7', occurredAt: '2026-09-14T08:00:00Z'
    }] };
    function element() {
        const children = new Map();
        return {
            dataset: {}, textContent: '', innerHTML: '', hidden: false,
            setAttribute() {}, remove() { elements.delete(this.id); },
            querySelector(selector) {
                if (!children.has(selector)) children.set(selector, element());
                return children.get(selector);
            }
        };
    }
    for (const id of ['notification-list', 'notification-count', 'notification-read-badge', 'notification-summary'])
        elements.set(id, element());
    const document = {
        body: { appendChild(node) { elements.set(node.id, node); } },
        createElement: element,
        getElementById: id => elements.get(id) || null,
        querySelector: () => null,
        addEventListener(type, handler) { listeners.set(type, handler); }
    };
    class AudioContext {
        state = 'running'; currentTime = 0;
        async resume() { this.state = 'running'; }
        createOscillator() { return { frequency: {}, connect() {}, disconnect() {}, start() { starts++; }, stop() {} }; }
        createGain() { return { gain: { setValueAtTime() {}, linearRampToValueAtTime() {}, exponentialRampToValueAtTime() {} }, connect() {}, disconnect() {} }; }
    }
    const context = vm.createContext({
        document, AudioContext, Map, Set, Number, String,
        apiFetch: async () => structuredClone(feed),
        apiPost: async (_, request) => { feed.items.find(item => item.notificationId === request.notificationId).isRead = true; feed.count--; },
        renderIcons() {}, renderAccountNotificationList() {}, showToast() {},
        escapeHtml: value => String(value ?? '').replaceAll('<', '&lt;').replaceAll('>', '&gt;'),
        formatTashkentDateTime: value => value,
        setTimeout() { timedCallbacks++; },
        openAccountNotification() {}
    });
    context.window = context;
    vm.runInContext(source + '\nwindow.load = loadAccountNotifications;', context);
    return {
        feed, elements, context, load: (announce = true) => context.load(announce),
        gesture: () => listeners.get('pointerdown')(),
        popup: () => elements.get('notification-announcement'),
        starts: () => starts, timedCallbacks: () => timedCallbacks
    };
}

test('announcement contains task details and has no expiry timer', async () => {
    const view = page();
    await view.load();
    assert.match(view.popup().innerHTML, /New task/);
    assert.match(view.popup().innerHTML, /Camera 7: check power/);
    assert.match(view.popup().innerHTML, /alert=7/);
    assert.match(view.popup().innerHTML, /notification-announcement-icon/);
    assert.doesNotMatch(view.popup().innerHTML, /notification-announcement-heading|Открыть карточку|arrow-up-right/);
    assert.doesNotMatch(view.popup().innerHTML, /Ещё /);
    assert.equal(view.timedCallbacks(), 0);
    await view.load();
    assert.ok(view.popup());
});

test('sound waits for browser user gesture and does not replay on unchanged feed', async () => {
    const view = page();
    await view.load();
    assert.equal(view.starts(), 0);
    await view.gesture();
    assert.equal(view.starts(), 2);
    await view.load();
    await view.gesture();
    assert.equal(view.starts(), 2);
});

test('clicking dismiss marks read, removes popup and leaves read-unresolved badge', async () => {
    const view = page();
    await view.load();
    await view.popup().querySelector('button').onclick();
    assert.equal(view.popup(), undefined);
    assert.equal(view.feed.items[0].isRead, true);
    assert.equal(view.elements.get('notification-read-badge').hidden, false);
    assert.equal(view.elements.get('notification-read-badge').textContent, '1');
});

test('new arrivals queue behind the current announcement instead of replacing it', async () => {
    const view = page();
    await view.load();
    await view.gesture();
    view.feed.items.push({ ...view.feed.items[0], notificationId: 'n:8', title: 'Second task' });
    view.feed.count = 2;
    await view.load();
    assert.match(view.popup().innerHTML, /New task/);
    assert.doesNotMatch(view.popup().innerHTML, /Second task/);
    assert.match(view.popup().innerHTML, /Ещё 1/);
    assert.equal(view.starts(), 4);
    await view.popup().querySelector('button').onclick();
    assert.match(view.popup().innerHTML, /Second task/);
});

test('account preferences can disable sound and popup without hiding the bell count', async () => {
    const view = page({ soundEnabled: false, popupEnabled: false });
    await view.load();
    await view.gesture();
    assert.equal(view.starts(), 0);
    assert.equal(view.popup(), undefined);
    assert.equal(view.elements.get('notification-count').textContent, '1');
});
