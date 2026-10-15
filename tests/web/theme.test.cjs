const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/wwwroot/js/theme.js'), 'utf8');

function theme(initial = 'light', blockedStorage = false, readyState = 'complete') {
    const makeElement = () => ({ attributes: {}, setAttribute(name, value) { this.attributes[name] = value; } });
    const buttons = [makeElement(), makeElement()];
    const color = makeElement();
    const html = { dataset: { theme: initial }, style: {} };
    const saved = [], listeners = new Map();
    const context = vm.createContext({
        window: {},
        document: { readyState, documentElement: html, querySelectorAll: () => buttons, querySelector: () => color,
            addEventListener: (event, callback) => listeners.set(event, callback) },
        localStorage: { setItem(key, value) { if (blockedStorage) throw new Error('Storage blocked'); saved.push([key, value]); } }
    });
    vm.runInContext(source, context);
    return { buttons, color, html, saved, listeners, toggle: context.window.toggleColorTheme };
}

test('theme toggles both directions and updates all controls and the saved preference', () => {
    const page = theme();
    page.toggle();
    assert.equal(page.html.dataset.theme, 'dark');
    assert.equal(page.html.style.colorScheme, 'dark');
    assert.equal(page.buttons[0].attributes['aria-pressed'], 'true');
    assert.equal(page.buttons[1].attributes.title, page.buttons[0].attributes['aria-label']);
    assert.deepEqual(page.saved[0], ['cm_theme', 'dark']);
    page.toggle();
    assert.equal(page.html.dataset.theme, 'light');
    assert.equal(page.buttons[0].attributes['aria-pressed'], 'false');
});

test('blocked browser storage does not break changing the theme', () => {
    const page = theme('dark', true);
    assert.doesNotThrow(page.toggle);
    assert.equal(page.html.dataset.theme, 'light');
});

test('theme controls initialize after document content becomes ready', () => {
    const page = theme('dark', false, 'loading');
    assert.equal(page.buttons[0].attributes.title, undefined);
    page.listeners.get('DOMContentLoaded')();
    assert.equal(page.buttons[0].attributes['aria-pressed'], 'true');
    assert.equal(page.color.attributes.content, '#111315');
});
