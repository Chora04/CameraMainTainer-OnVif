const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');
const razor = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/Pages/Cameras/Index.cshtml'), 'utf8');
const source = razor.slice(razor.indexOf('    function cameraCoordinatesChanged('), razor.indexOf('    async function saveCamera('));
function fixture(apiFetch) {
    const fields = Object.fromEntries(['camera-latitude', 'camera-longitude', 'camera-street-address', 'camera-location-status', 'camera-location-lookup', 'camera-editor'].map(id => [id, { value: '', textContent: '', disabled: false, hidden: false }]));
    const runtime = vm.createContext({ cameraLocationRequest: 0, apiFetch, document: { getElementById: id => fields[id] } });
    vm.runInContext(source, runtime);
    return { runtime, fields };
}
test('coordinates distinguish empty values from zero', () => {
    const { runtime, fields } = fixture();
    assert.equal(runtime.cameraLocationInput().latitude, null);
    fields['camera-latitude'].value = '0';
    fields['camera-longitude'].value = '0';
    assert.equal(runtime.cameraLocationInput().latitude, 0);
    assert.equal(runtime.cameraLocationInput().longitude, 0);
});
test('reverse lookup validates coordinates before sending any request', async () => {
    const { runtime, fields } = fixture(() => { throw Error('must not call'); });
    fields['camera-latitude'].value = '91'; fields['camera-longitude'].value = '69';
    await runtime.resolveCameraStreet();
    assert.ok(fields['camera-location-status'].textContent.includes('-90'));
});
test('changed coordinates invalidate a pending result and clear the old street', async () => {
    let finish;
    const { runtime, fields } = fixture(() => new Promise(resolve => finish = resolve));
    fields['camera-latitude'].value = '41'; fields['camera-longitude'].value = '69';
    const work = runtime.resolveCameraStreet();
    fields['camera-latitude'].value = '42';
    runtime.cameraCoordinatesChanged();
    finish({ streetAddress: 'Old street' }); await work;
    assert.equal(fields['camera-street-address'].value, '');
    assert.equal(fields['camera-location-lookup'].disabled, false);
});
test('lookup applies a result but never overwrites a manual edit', async () => {
    let finish;
    const { runtime, fields } = fixture(() => new Promise(resolve => finish = resolve));
    fields['camera-latitude'].value = '41'; fields['camera-longitude'].value = '69';
    const work = runtime.resolveCameraStreet();
    fields['camera-street-address'].value = 'Manual street';
    finish({ streetAddress: 'Found street' }); await work;
    assert.equal(fields['camera-street-address'].value, 'Manual street');
    const second = runtime.resolveCameraStreet();
    finish({ streetAddress: 'Found street' }); await second;
    assert.equal(fields['camera-street-address'].value, 'Found street');
});
