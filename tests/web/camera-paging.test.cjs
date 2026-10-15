const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const page = fs.readFileSync(path.resolve(__dirname, '../../src/CameraMaintainer.Web/Pages/Cameras/Index.cshtml'), 'utf8');
const refresh = page.slice(page.indexOf('    function refreshCameraPageData('), page.indexOf('    function handleInspectorFrameLoad('));

function setup() {
    const requests = [];
    let registryRefreshes = 0;
    const context = vm.createContext({
        cameraPageRefreshPromise: null, selectedCameraId: 0, canViewCameraAlertHistory: false,
        URLSearchParams, console,
        document: { querySelectorAll: () => [1, 2, 20000].map(id => ({ dataset: { cameraRow: String(id) } })) },
        apiFetch: async url => { requests.push(url); return []; },
        updateCameraRegistryRows: () => {},
        refreshCameraRegistry: async () => { registryRefreshes++; }
    });
    vm.runInContext(refresh, context);
    return { context, requests, registryRefreshes: () => registryRefreshes };
}

test('camera refresh batches only visible changed IDs, including devices beyond 1000', async () => {
    const f = setup();
    await f.context.refreshCameraPageData({ topics: ['cameras'], cameraIds: [20000, 19999] });
    assert.deepEqual(f.requests, ['/api/cameras/lookup?ids=20000']);
});

test('updates outside the current camera page do not request camera data', async () => {
    const f = setup();
    await f.context.refreshCameraPageData({ topics: ['cameras'], cameraIds: [19999] });
    assert.deepEqual(f.requests, []);
});

test('fallback requests one bounded visible-ID batch, not the entire registry', async () => {
    const f = setup();
    await f.context.refreshCameraPageData();
    assert.deepEqual(f.requests, ['/api/cameras/lookup?ids=1&ids=2&ids=20000']);
});

test('registry replacement already contains rows, so it does not fetch them twice', async () => {
    const f = setup();
    await f.context.refreshCameraPageData({ topics: ['registry', 'cameras'] });
    assert.equal(f.registryRefreshes(), 1);
    assert.deepEqual(f.requests, []);
});

test('manual camera list checks use the server CPU limit rather than a fixed eight', async () => {
    const code = page.slice(page.indexOf('    async function checkIds('), page.indexOf('    function checkSelectedCameras('));
    for (const limit of [2, 8, 16]) {
        let running = 0, peak = 0, calls = 0;
        const context = vm.createContext({
            cameraCheckConcurrency: limit,
            document: { getElementById: () => null, querySelector: () => null },
            apiFetch: async () => {
                peak = Math.max(peak, ++running);
                calls++;
                await new Promise(resolve => setImmediate(resolve));
                running--;
            },
            showToast: () => {}, refreshCameraPageData: async () => {}
        });
        vm.runInContext(code, context);
        await context.checkIds(Array.from({ length: 50 }, (_, index) => index + 1));
        assert.equal(peak, limit);
        assert.equal(calls, 50);
        assert.equal(running, 0);
    }
});
