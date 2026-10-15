const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const root = path.join(__dirname, '../../src/CameraMaintainer.Web');
const dashboard = fs.readFileSync(path.join(root, 'Pages/Dashboard.cshtml'), 'utf8');
const cameras = fs.readFileSync(path.join(root, 'Pages/Cameras/Index.cshtml'), 'utf8');
const layout = fs.readFileSync(path.join(root, 'Pages/_Layout.cshtml'), 'utf8');
const app = fs.readFileSync(path.join(root, 'wwwroot/js/app.js'), 'utf8');

test('camera workspace combines summary and registry without attention or duplicate navigation', () => {
    assert.ok(dashboard.includes('@model CameraMaintainer.Web.Pages.DashboardModel'));
    assert.ok(!cameras.includes('runFullCheck'));
    assert.ok(cameras.includes('checkCurrentCamera(this)'));
    assert.ok(cameras.includes('data-summary-value="total"'));
    assert.ok(!cameras.includes('_CameraAttention'));
    assert.ok(!app.includes('refreshCameraAttention'));
    assert.ok(layout.includes('title="Все ошибки"'));
    assert.ok(cameras.includes('camera-alert-history-launch'));
    assert.ok(!layout.includes('title="Обзор"'));
});

test('summary refresh fetches only counters and does not duplicate registry lookups', async () => {
    const summaryRefresh = app.slice(app.indexOf('    async function refreshCameraSummary('), app.indexOf('    function updateSummaryValue('));
    const paths = [];
    let rendered;
    const context = vm.createContext({
        document: { querySelector: () => ({}) },
        apiFetch: async url => { paths.push(url); return { total: 5 }; },
        applyHealthSummary: summary => { rendered = summary.total; }
    });
    vm.runInContext(summaryRefresh, context);
    await context.refreshCameraSummary({ topics: ['alerts'] });
    assert.deepEqual(paths, []);
    await context.refreshCameraSummary({ topics: ['cameras'] });
    assert.deepEqual(paths, ['/api/health/status']);
    assert.equal(rendered, 5);
});
