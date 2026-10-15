const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const alerts = read('src/CameraMaintainer.Web/Pages/Alerts/Index.cshtml');
const cameras = read('src/CameraMaintainer.Web/Pages/Cameras/Index.cshtml');
const css = read('src/CameraMaintainer.Web/wwwroot/css/ops-dense.css');
const scrollCode = alerts.slice(alerts.indexOf('    function scrollAlertBoard('), alerts.indexOf('    const errorManagers'));

test('board arrows scroll one actual column including its gap in either direction', () => {
    const scrolls = [];
    const board = {
        querySelector: () => ({ getBoundingClientRect: () => ({ width: 324 }) }),
        scrollBy: options => scrolls.push(options)
    };
    const context = vm.createContext({
        document: { querySelector: () => board },
        getComputedStyle: () => ({ columnGap: '16px' })
    });
    vm.runInContext(scrollCode, context);
    context.scrollAlertBoard(1);
    context.scrollAlertBoard(-1);
    assert.equal(scrolls[0].left, 340);
    assert.equal(scrolls[1].left, -340);
    assert.equal(scrolls[0].behavior, 'smooth');
});

test('board arrows are harmless on an empty board or history', () => {
    for (const board of [null, { querySelector: () => null }]) {
        const context = vm.createContext({ document: { querySelector: () => board } });
        vm.runInContext(scrollCode, context);
        assert.doesNotThrow(() => context.scrollAlertBoard(1));
    }
});

test('moving camera diagnostics preserves each live-update target exactly once', () => {
    const ids = [...cameras.matchAll(/id="(metric-[a-z-]+)"/g)].map(match => match[1]);
    const expected = ['device', 'onvif', 'camera-time', 'firmware', 'drift',
        'lens', 'lens-evidence', 'status', 'brightness', 'contrast', 'edges', 'entropy', 'ping', 'last-check', 'jitter', 'packet-loss', 'snapshot', 'snapshot-size',
        'street-address']
        .map(name => 'metric-' + name);
    assert.deepEqual(ids.sort(), expected.sort());
    assert.match(cameras, /class="preview-diagnostics"/);
    assert.match(cameras, /onclick="openCameraAlertHistoryPopup\(\)"/);
});

test('selected camera header groups name and location without IP or serial diagnostics', () => {
    assert.match(cameras, /class="inspector-identity"><h2 id="inspector-name"/);
    assert.match(cameras, /id="inspector-coordinates" target="_blank" rel="noopener noreferrer"[^>]*hidden/);
    assert.match(cameras, /coordinates\.textContent = hasCoordinates \? latitude \+ ', ' \+ longitude/);
    assert.match(cameras, /coordinates\.href = 'https:\/\/www\.openstreetmap\.org\/\?mlat='/);
    assert.doesNotMatch(cameras, /id="metric-(ip|serial|latitude|longitude)"/);
    assert.match(css, /\.inspector-identity\s*\{\s*display: flex;/);
});

test('workspace keeps partial-refresh boundaries and local font assets', () => {
    for (const hook of ['queue-summary', 'queue-toolbar', 'alert-results']) {
        assert.ok(alerts.includes(hook));
    }
    assert.match(alerts, /class="queue-work-area"/);
    assert.match(css, /repeat\(var\(--board-column-count\), minmax\(300px, 1fr\)\)/);
    assert.match(css, /font-display: swap/);
    assert.ok(fs.statSync(path.join(root, 'src/CameraMaintainer.Web/wwwroot/fonts/Manrope-Variable.ttf')).size > 10000);
    assert.ok(fs.existsSync(path.join(root, 'src/CameraMaintainer.Web/wwwroot/fonts/Manrope-OFL.txt')));
});

test('queue overview sits above the full-width board with equally sized counters', () => {
    const activeCss = css.replace(/\/\*[\s\S]*?\*\//g, '');
    assert.ok(alerts.indexOf('class="queue-overview"') < alerts.indexOf('class="queue-work-area"'));
    assert.match(activeCss, /\.queue-workspace\s*\{[^}]*grid-template-columns:\s*minmax\(0, 1fr\);/);
    assert.match(activeCss, /\.queue-work-area\s*\{\s*min-width:\s*0;/);
    assert.match(activeCss, /\.queue-summary\s*\{[^}]*grid-template-columns:\s*repeat\(4, minmax\(0, 1fr\)\);[^}]*margin:\s*0;/);
    assert.match(activeCss, /\.queue-summary\.history-disabled\s*\{[^}]*grid-template-columns:\s*repeat\(3, minmax\(0, 1fr\)\);/);
});

test('desktop active board fills the remaining viewport height', () => {
    assert.match(css, /@media \(min-width:\s*861px\)\s*\{\s*\.error-crm-board\s*\{[^}]*height:\s*max\(320px,\s*calc\(100dvh - 340px\)\);/);
    assert.match(css, /\.error-crm-board\s*\{[^}]*overflow-y:\s*auto;/);
});

test('compact ticket keeps a translucent fault surface above the light start-time row', () => {
    assert.match(css, /\.error-crm-summary-error\s*\{[^}]*margin:\s*0;[^}]*background:\s*rgba\(/);
    assert.match(css, /\.error-crm-summary-error\s*\{[^}]*align-items:\s*stretch;[^}]*column-gap:\s*4px;[^}]*padding:\s*4px;/);
    assert.match(css, /\.error-crm-summary-error-title\s*\{[^}]*justify-self:\s*stretch;[^}]*width:\s*100%;[^}]*background:\s*color-mix\([^;]*transparent\);[^}]*border:\s*1px solid color-mix\(/);
    assert.match(css, /\.error-crm-ticket\.warning\s+\.error-crm-summary-error\s*\{[^}]*background:\s*rgba\(240, 173, 53, \.\d+\)/);
    assert.match(css, /\.error-crm-ticket\.critical\s+\.error-crm-summary-error\s*\{[^}]*background:\s*rgba\(239, 101, 117, \.\d+\)/);
    assert.match(css, /\.error-crm-summary-camera\s*\{[^}]*background:\s*color-mix\(/);
    assert.match(css, /\.error-crm-summary-count\s*\{[^}]*background:\s*color-mix\([^;]*transparent\);/);
    assert.match(css, /\.error-crm-issue-picker\s*\{[^}]*align-self:\s*end;[^}]*justify-self:\s*end;/);
    assert.match(css, /\.ticket-summary-footer\s*\{[^}]*background:\s*var\(--panel\);/);
    assert.match(css, /\.ticket-summary-time strong\s*\{[^}]*color:\s*var\(--text\);/);
});

test('history uses three concise cards without repeating the camera', () => {
    const start = alerts.indexOf('<article class="error-item history-record');
    const end = alerts.indexOf('</article>', start);
    const historyCard = alerts.slice(start, end);
    const heading = historyCard.slice(0, historyCard.indexOf('</header>'));
    assert.match(css, /\.error-list:has\(> \.history-record\)\s*\{[^}]*grid-template-columns:\s*repeat\(3, minmax\(0, 1fr\)\);/);
    assert.match(historyCard, /class="history-error-highlight"/);
    assert.match(historyCard, /class="history-meta-column history-meta-period"/);
    assert.match(historyCard, /class="history-meta-column history-meta-people"/);
    assert.doesNotMatch(historyCard, /class="error-camera-value"/);
    assert.equal((alerts.match(/<partial name="_AlertIssues"/g) || []).length, 1);
    assert.match(heading, /class="error-number"[^>]*>№ @serialNumber<\/span>/);
    assert.doesNotMatch(heading, /openTransferredAlert/);
    assert.ok(historyCard.indexOf('history-record-actions') > historyCard.indexOf('error-audit-panel'));
    assert.match(css, /\.error-item\.history-record\s*\{[^}]*height:\s*100%;/);
    assert.match(css, /\.history-record-actions\s*\{[^}]*margin-top:\s*auto;/);
    assert.match(css, /\.history-error-highlight\s*\{[^}]*width:\s*fit-content;[^}]*max-width:\s*100%;/);
    assert.match(css, /\.history-record \.error-metadata\s*\{[^}]*display:\s*grid;[^}]*grid-template-columns:\s*repeat\(2, minmax\(0, 1fr\)\);/);
});

test('active ticket shows selected issue position and exact total in the title row', () => {
    assert.match(alerts, /var issueCount = Math\.Max\(1, alert\.Issues\.Count\);/);
    assert.match(alerts, /class="error-crm-summary-error"[\s\S]*?class="error-crm-summary-count"[^>]*data-issue-total="@issueCount"[\s\S]*?<span class="error-crm-summary-position">1 из @issueCount<\/span>/);
    assert.match(alerts, /data-issue-position="@\(issueIndex \+ 1\)"/);
    assert.match(alerts, /positionTarget\.textContent = `\$\{position\} из \$\{total\}`/);
    assert.match(alerts, /class="error-crm-summary-type @primaryCategoryClass">@primaryCategoryLabel<\/span>/);
    assert.match(alerts, /data-issue-category="@issueCategoryClass" data-issue-category-label="@issueCategoryLabel"/);
    assert.match(alerts, /categoryBadge\.classList\.toggle\('physical', category === 'physical'\)/);
    assert.doesNotMatch(alerts, /class="error-crm-summary-type physical">Физическая<\/span>\s*<span class="error-crm-summary-type technical">Техническая<\/span>/);
    assert.match(alerts, /class="ticket-summary-footer"/);
    assert.match(alerts, /class="error-crm-issue-menu"[^>]*role="listbox"/);
    assert.match(alerts, /data-issue-title="@issueTitle"/);
    assert.match(alerts, /function selectAlertIssue\(event, option\)/);
    assert.match(css, /\.error-crm-issue-menu\s*\{[^}]*grid-column:\s*1\s*\/\s*-1;[^}]*position:\s*static;/);
    assert.doesNotMatch(css, /\.error-crm-ticket\.issue-menu-open/);
    assert.doesNotMatch(alerts, /id="alert-selection-bar"|data-alert-select/);
    assert.doesNotMatch(alerts, /alert\.Issues\.Count - 1/);
    assert.doesNotMatch(alerts, /Ошибок: @issueCount/);
});
