const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/wwwroot/js/realtime.js'), 'utf8');

function page() {
    const events = {};
    const form = { isConnected: true };
    const state = { clears: 0, closes: 0, reloads: 0, version: 'original' };
    const context = vm.createContext({
        document: {
            body: { dataset: { accessVersion: 'original' } },
            addEventListener: (name, handler) => { events[name] = handler; },
            getElementById: id => id === 'form' ? form : null,
            querySelector: () => ({ replaceChildren: () => state.clears++ })
        },
        apiFetch: async () => ({ version: state.version }),
        closeModal: () => state.closes++,
        location: { reload: () => state.reloads++ }
    });
    context.window = context;
    vm.runInContext(source, context);
    return { context, form, state, edit: () => events.input({ target: { closest: () => form } }) };
}

test('live form data applies only when the form has no unsaved edits', () => {
    const p = page();
    const snapshot = p.context.captureLiveForm('form');
    assert.equal(p.context.canApplyLiveForm(snapshot), true);
    p.edit();
    assert.equal(p.context.canApplyLiveForm(snapshot), false);
    assert.equal(p.context.canApplyLiveForm(p.context.captureLiveForm('form')), false);
    p.context.markLiveFormSaved(p.context.captureLiveForm('form'));
    assert.equal(p.context.canApplyLiveForm(p.context.captureLiveForm('form')), true);
});

test('a save response cannot mark newer typing as saved or revive a closed editor', () => {
    const p = page();
    p.edit();
    const saving = p.context.captureLiveForm('form');
    p.edit();
    p.context.markLiveFormSaved(saving);
    assert.equal(p.context.canApplyLiveForm(p.context.captureLiveForm('form')), false);
    p.context.markLiveFormSaved(p.context.captureLiveForm('form'));
    p.form.isConnected = false;
    assert.equal(p.context.canApplyLiveForm(p.context.captureLiveForm('form')), false);
});

test('unchanged permissions do not reload; changed permissions clear protected data first', async () => {
    const p = page();
    assert.equal(await p.context.refreshWorkspaceAccess(), true);
    assert.equal(p.state.reloads, 0);
    p.state.version = 'revoked';
    assert.equal(await p.context.refreshWorkspaceAccess(), false);
    assert.equal(p.state.clears, 1);
    assert.equal(p.state.closes, 1);
    assert.equal(p.state.reloads, 1);
});

test('unavailable forms are harmless to a refresh', () => {
    const p = page();
    assert.equal(p.context.canApplyLiveForm(p.context.captureLiveForm('absent')), false);
});
