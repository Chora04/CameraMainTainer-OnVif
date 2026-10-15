const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const app = fs.readFileSync(path.join(root, 'src/CameraMaintainer.Web/wwwroot/js/app.js'), 'utf8');
const page = fs.readFileSync(path.join(root, 'src/CameraMaintainer.Web/Pages/Cameras/Index.cshtml'), 'utf8');
const math = vm.createContext({});
vm.runInContext(app.slice(app.indexOf('    const lensClamp'), app.indexOf('    function analyzeLensImage')) + '\nglobalThis.assess = assessLensMetrics;', math);

function diagnostics() {
    const values = {}, evidence = { textContent: '' };
    const context = vm.createContext({
        CameraLensMath: { assess: math.assess },
        document: { getElementById: () => evidence },
        setMetric: (id, value, tone) => { values[id] = { value, tone }; }
    });
    vm.runInContext(page.slice(page.indexOf('    function fixed('), page.indexOf('    async function selectCamera(')), context);
    return { context, values, evidence };
}

test('missing image measurements are not converted to zero or a healthy scene', () => {
    for (const value of [null, undefined, '', NaN]) {
        assert.equal(math.assess({ mean: value, entropy: 7, edges: 20 }).available, false);
    }
    const dark = math.assess({ mean: 0, entropy: 0, edges: 0, stdDev: 0 });
    assert.equal(dark.available, true);
    assert.equal(dark.tone, 'danger');
});

test('image evidence uses full names and metrics retain the existing scoring', () => {
    const result = math.assess({ mean: 110, entropy: 7, edges: 20, stdDev: 40 });
    assert.equal(result.tone, 'ok');
    assert.equal(result.risk, 0.25);
    for (const label of ['Яркость', 'Контраст', 'Энтропия', 'Доля контуров']) assert.ok(result.evidence.includes(label));
    assert.ok(!result.evidence.includes('μ'));
});

test('unavailable frames have no numeric evidence; live frame metrics include each field', () => {
    const { context, values, evidence } = diagnostics();
    const missing = context.lensInfo({ SnapshotSizeBytes: 0, MeanBrightness: 0, ImageEntropy: 0, EdgeDensity: 0 });
    assert.equal(missing.available, false);
    context.setLensAssessment(missing);
    assert.equal(values['metric-brightness'].value, 'Нет данных');
    context.setLensAssessment(math.assess({ mean: 110, entropy: 7, edges: 20, stdDev: 40 }), 'По показанному кадру');
    assert.equal(values['metric-brightness'].value, '110.0 из 255');
    assert.equal(values['metric-contrast'].value, '40.0');
    assert.equal(values['metric-edges'].value, '20.00%');
    assert.equal(values['metric-entropy'].value, '7.00 бит');
    assert.ok(evidence.textContent.startsWith('По показанному кадру'));
});

test('missing network measurements are not displayed as zero; real zero is valid', () => {
    const { context, values } = diagnostics();
    context.setNetworkDiagnostics({ PingAvgMs: null, JitterMs: null, PacketLossPercent: null, SnapshotSizeBytes: 0, SnapshotDownloadMs: 0 });
    assert.equal(values['metric-ping'].value, 'Нет ответа или нет измерения');
    assert.equal(values['metric-jitter'].value, 'Недостаточно ответов');
    assert.equal(values['metric-packet-loss'].value, 'Нет измерения');
    assert.equal(values['metric-snapshot'].value, 'Нет измерения');
    context.setNetworkDiagnostics({ PingAvgMs: 0, JitterMs: 0, PacketLossPercent: 0, SnapshotSizeBytes: 2048, SnapshotDownloadMs: 0 });
    assert.equal(values['metric-ping'].value, '0.0 мс');
    assert.equal(values['metric-jitter'].value, '0.0 мс');
    assert.equal(values['metric-packet-loss'].tone, 'ok');
    assert.equal(values['metric-snapshot-size'].value, '2.0 КБ');
    context.setNetworkDiagnostics({ PacketLossPercent: 100 });
    assert.equal(values['metric-packet-loss'].tone, 'warning');
});

test('frame size stays compact without a meaningless universal-norm reference', () => {
    const section = page.slice(page.indexOf('<section class="preview-diagnostics"'), page.indexOf('</aside>', page.indexOf('<section class="preview-diagnostics"')));
    const itemCount = (section.match(/class="telemetry-item/g) || []).length;
    assert.equal(itemCount, 10);
    assert.equal((section.match(/class="metric-reference"/g) || []).length, itemCount - 1);
    assert.match(section, /class="telemetry-item metric-without-reference"/);
    assert.doesNotMatch(section, /Нет единой нормы/);
});
