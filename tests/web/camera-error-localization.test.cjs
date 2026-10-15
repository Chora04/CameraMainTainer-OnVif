const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/wwwroot/js/app.js'), 'utf8');
const source = app.slice(app.indexOf('    window.cameraErrorMessages ='), app.indexOf('    window.renderIcons ='));
const timeout = 'Превышено время ожидания ответа камеры. Проверка не завершилась за отведённое время.';
function translate(catalog) {
    const runtime = { window: {}, document: { getElementById: () => ({ textContent: JSON.stringify(catalog) }) } };
    vm.runInNewContext(source, runtime);
    return runtime.window.localizeCameraError;
}

test('live card timeout text uses the server translation catalog', () => {
    const localize = translate({ CameraCheckTimeout: timeout });
    for (const message of ['timeout: A task was canceled.', ' TIMEOUT: canceled ', 'A task was canceled.', 'A task was cancelled.']) {
        assert.equal(localize(message), timeout);
    }
    assert.equal(localize('connection_refused: details'), 'connection_refused: details');
    assert.equal(localize(timeout), timeout);
    assert.equal(localize(null), '');
});

test('changing the catalog changes the language without changing fault data', () => {
    const message = 'timeout: A task was canceled.';
    assert.equal(translate({ CameraCheckTimeout: 'Camera response timed out.' })(message), 'Camera response timed out.');
    assert.equal(translate({})(message), message);
});
