const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/wwwroot/js/app.js'), 'utf8');
const start = app.indexOf('    const cameraMessage =');
const end = app.indexOf('    window.renderCameraCaseIssues =', start);
const source = app.slice(start, end);

function runtime(messages = {}) {
    const escapeHtml = value => String(value).replace(/[&<>"']/g, character => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    })[character]);
    const context = vm.createContext({
        cameraErrorMessages: messages,
        formatTashkentDateTime: value => String(value),
        window: {
            cameraErrorMessages: messages,
            escapeHtml,
            localizeCameraError: value => String(value || '')
        }
    });
    vm.runInContext(source, context);
    return context.window;
}

test('legacy timeout explains known cause without inventing missing stage or duration', () => {
    const diagnosis = runtime().getCameraIssueDiagnosis({
        description: 'timeout: A task was canceled.',
        recoveredAt: '2026-09-04T06:46:55Z'
    });

    assert.equal(diagnosis.reason, 'Камера не ответила за отведённое время');
    assert.equal(diagnosis.stage, 'Не зафиксирован в старой записи');
    assert.match(diagnosis.timing, /точное время в старой записи не сохранено/i);
    assert.match(diagnosis.result, /Камера снова отвечает/);
});

test('stored diagnostic shows ONVIF stage, actual duration and configured timeout', () => {
    const diagnosis = runtime().getCameraIssueDiagnosis({
        failureCode: 'timeout',
        failureStage: 'onvif_device_info',
        failureDurationMs: 10014,
        failureTimeoutMs: 10000
    });

    assert.equal(diagnosis.stage, 'Получение основных данных ONVIF');
    assert.match(diagnosis.timing, /10(?:[,.]\d)? сек при лимите 10 сек/);
    assert.match(diagnosis.result, /остаётся активной/);
});

test('ordinary camera issue without check diagnostics stays compact', () => {
    assert.equal(runtime().getCameraIssueDiagnosis({
        description: 'Temperature is high',
        failureDurationMs: null
    }), null);
});

test('diagnostic labels and reasons come from the localization catalog', () => {
    const view = runtime({
        FailureReasonTimeout: 'CUSTOM REASON',
        DiagnosisReasonLabel: 'CUSTOM LABEL'
    });
    const diagnosis = view.getCameraIssueDiagnosis({ description: 'timeout: canceled' });
    const html = view.renderCameraIssueDiagnosis(diagnosis);

    assert.equal(diagnosis.reason, 'CUSTOM REASON');
    assert.match(html, /CUSTOM LABEL:/);
});
