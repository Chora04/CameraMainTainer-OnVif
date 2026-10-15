const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const razor = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/Pages/Alerts/Index.cshtml'), 'utf8');
const interactions = razor.slice(razor.indexOf('    let draggedAlertId = 0;'), razor.indexOf('    function openErrorEditor('));

function classes(initial = []) {
    const values = new Set(initial);
    return {
        add: value => values.add(value), remove: value => values.delete(value), contains: value => values.has(value),
        toggle(value, enabled) { if (enabled) values.add(value); else values.delete(value); }
    };
}

function board({ fail = false, writable = true } = {}) {
    const listeners = new Map();
    const requests = [], messages = [], openedCards = [];
    let refreshes = 0;
    const body = { classList: classes() };
    function column(key, count) {
        return {
            dataset: { alertStage: key }, classList: classes(), counter: { textContent: String(count) },
            style: { getPropertyValue: () => '#008080' },
            closest() { return this; }, contains: () => false,
            querySelector(selector) {
                if (selector === '.error-crm-stage-empty') return null;
                if (selector === '[data-alert-drop-stage]') return { appendChild: card => { card.column = this; } };
                if (selector === '.error-crm-ticket') return this === card.column ? card : null;
                if (selector === '.error-crm-stage-header > b') return this.counter;
            }
        };
    }
    const source = column('new', 1), target = column('waiting', 0);
    const card = {
        dataset: { alertId: '7', alertStage: 'new' }, open: false, draggable: writable,
        column: source, classList: classes(), attributes: {},
        style: { setProperty(name, value) { this[name] = value; } },
        setAttribute(name, value) { this.attributes[name] = value; }, removeAttribute(name) { delete this.attributes[name]; },
        closest(selector) {
            if (selector === '.error-crm-ticket' || selector === '.error-crm-ticket, .error-item') return this;
            if (selector.startsWith('.error-crm-stage')) return this.column;
            return null;
        }
    };
    const summary = { closest: selector => selector === '.error-crm-ticket-summary' ? summary : card.closest(selector) };
    const document = {
        body,
        addEventListener(type, handler) { listeners.set(type, [...(listeners.get(type) || []), handler]); },
        querySelector: () => card,
        querySelectorAll(selector) {
            if (selector.includes('ticket.dragging')) return card.classList.contains('dragging') ? [card] : [];
            return [source, target].filter(column => column.classList.contains('drag-over'));
        }
    };
    const context = vm.createContext({
        document, Set, Number, String,
        apiFetch: async (url, options) => { requests.push({ url, options }); if (fail) throw new Error('No permission'); return {}; },
        showToast: (message, type) => messages.push({ message, type }),
        openTransferredAlert: id => openedCards.push(id),
        refreshAlertBoard: async () => { refreshes++; },
        refreshVisibleData() {}, getSelection: () => ({ removeAllRanges() {} })
    });
    context.window = context;
    vm.runInContext(interactions, context);
    context.initAlertDragAndDrop();
    async function emit(type, targetElement = summary, detail = 1, isTrusted = true) {
        const event = {
            target: targetElement, detail, isTrusted, prevented: false, relatedTarget: null,
            dataTransfer: { setData() {} }, preventDefault() { this.prevented = true; }
        };
        for (const handler of listeners.get(type) || []) await handler(event);
        return event;
    }
    return { card, source, target, summary, context, requests, messages, listeners, emit, body, openedCards, refreshes: () => refreshes };
}

test('single mouse click does not expand; double click opens the full card without expanding the board', async () => {
    const page = board();
    assert.equal((await page.emit('click')).prevented, true);
    assert.equal(page.card.open, false);
    assert.deepEqual(page.openedCards, []);
    await page.emit('dblclick');
    assert.deepEqual(page.openedCards, [7]);
    assert.equal(page.card.open, false);
});

test('keyboard activation opens the same full card', async () => {
    const page = board();
    assert.equal((await page.emit('click', undefined, 0)).prevented, true);
    assert.deepEqual(page.openedCards, [7]);
});

test('expand button opens the full card with one click', async () => {
    const page = board();
    const button = {
        closest(selector) {
            if (selector === '.error-crm-summary-chevron') return button;
            if (selector === '.error-crm-ticket-summary') return page.summary;
            return page.card.closest(selector);
        }
    };
    assert.equal((await page.emit('click', button)).prevented, true);
    assert.deepEqual(page.openedCards, [7]);
});

test('programmatic expand-button click during refresh does not open the card', async () => {
    const page = board();
    const button = {
        closest(selector) {
            if (selector === '.error-crm-summary-chevron') return button;
            if (selector === '.error-crm-ticket-summary') return page.summary;
            return page.card.closest(selector);
        }
    };
    assert.equal((await page.emit('click', button, 0, false)).prevented, true);
    assert.deepEqual(page.openedCards, []);
});

test('reinitialization after partial refresh does not duplicate handlers', async () => {
    const page = board();
    page.context.initAlertDragAndDrop();
    assert.equal(page.listeners.get('drop').length, 1);
    assert.equal(page.listeners.get('dblclick').length, 1);
    await page.emit('dblclick');
    assert.deepEqual(page.openedCards, [7]);
});

test('dragging a card never opens its dialog', async () => {
    const page = board();
    await page.emit('dragstart');
    await page.emit('dblclick');
    assert.deepEqual(page.openedCards, []);
});

test('double click on an action button does not open the card', async () => {
    const page = board();
    const button = { closest: selector => selector.startsWith('button,') ? button : page.card.closest(selector) };
    await page.emit('dblclick', button);
    assert.deepEqual(page.openedCards, []);
});

test('dropping anywhere in a column saves the stage, moves card and updates color without reloading', async () => {
    const page = board();
    await page.emit('dragstart');
    assert.equal((await page.emit('dragover', page.target)).prevented, true);
    await page.emit('drop', page.target);
    assert.equal(page.requests.length, 1);
    assert.equal(page.requests[0].url, '/api/alerts/7/stage');
    assert.equal(page.requests[0].options.body.stage, 'waiting');
    assert.equal(page.card.column, page.target);
    assert.equal(page.card.dataset.alertStage, 'waiting');
    assert.equal(page.card.style['--error-stage-color'], '#008080');
    assert.equal(page.source.counter.textContent, '0');
    assert.equal(page.target.counter.textContent, '1');
    assert.equal(page.refreshes(), 1);
    assert.equal(page.body.classList.contains('alert-moving'), false);
});

test('failed stage save leaves the card in its original column and reports the error', async () => {
    const page = board({ fail: true });
    await page.emit('dragstart');
    await page.emit('drop', page.target);
    assert.equal(page.card.column, page.source);
    assert.equal(page.card.dataset.alertStage, 'new');
    assert.equal(page.messages[0].message, 'No permission');
    assert.equal(page.messages[0].type, 'error');
});

test('read-only cards cannot start dragging', async () => {
    const page = board({ writable: false });
    assert.equal((await page.emit('dragstart')).prevented, true);
    await page.emit('drop', page.target);
    assert.equal(page.requests.length, 0);
});

test('dropping back into the original column does not send a write', async () => {
    const page = board();
    await page.emit('dragstart');
    await page.emit('drop', page.source);
    assert.equal(page.requests.length, 0);
});

test('rendered Razor scripts have valid JavaScript syntax', {
    skip: process.env.CAMERAMAINTAINER_RENDERED_MARKUP_DIR
        ? false
        : 'Rendered markup is generated by scripts/verify.ps1'
}, () => {
    const renderedMarkupDirectory = process.env.CAMERAMAINTAINER_RENDERED_MARKUP_DIR;
    for (const role of ['alerts-admin', 'alerts-technician', 'settings-admin', 'cameras-admin', 'cameras-technician',
        'history-admin', 'history-camera-admin', 'detail-admin', 'users-admin', 'login']) {
        const file = path.join(renderedMarkupDirectory, role + '.html');
        const html = fs.readFileSync(file, 'utf8');
        for (const [, attributes, script] of html.matchAll(/<script(\s[^>]*)?>([\s\S]*?)<\/script>/g)) {
            if (attributes?.includes('application/json')) assert.doesNotThrow(() => JSON.parse(script));
            else assert.doesNotThrow(() => new vm.Script(script, { filename: role + '.js' }));
        }
    }
});
