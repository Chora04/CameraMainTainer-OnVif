const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const root = path.join(__dirname, '../..');
const layout = fs.readFileSync(path.join(root, 'src/CameraMaintainer.Web/Pages/_Layout.cshtml'), 'utf8');
const app = fs.readFileSync(path.join(root, 'src/CameraMaintainer.Web/wwwroot/js/app.js'), 'utf8');
const styles = fs.readFileSync(path.join(root, 'src/CameraMaintainer.Web/wwwroot/css/ops-dense.css'), 'utf8');

test('shared planner exposes a calendar, agenda and editor from every alerts-enabled page', () => {
    for (const id of ['workspace-planner-popover', 'workspace-calendar-grid', 'workspace-note-list', 'workspace-note-form'])
        assert.match(layout, new RegExp(`id="${id}"`));
    assert.match(layout, /data-can-edit="@canWorkAlerts/);
    assert.match(layout, /var canWorkAlerts = canViewAlerts &&\s*User\.HasClaim\(AccessPolicies\.PermissionClaim, AccessPolicies\.RunChecks\)/);
    assert.doesNotMatch(layout, /HasClaim\(AccessPolicies\.PermissionClaim, AccessPolicies\.WorkAlerts\)/);
    assert.match(styles, /\.workspace-calendar-grid[^}]*grid-template-columns:\s*repeat\(7,/);
    assert.match(styles, /\.workspace-planner-body[^}]*grid-template-columns:/);
});

test('planner uses Tashkent dates and supports create, edit, complete and delete actions', () => {
    assert.match(app, /timeZone:\s*'Asia\/Tashkent'/);
    assert.match(app, /\/api\/workspace-notes/);
    for (const action of ['saveWorkspaceNote', 'editWorkspaceNote', 'toggleWorkspaceNoteComplete', 'deleteWorkspaceNote'])
        assert.match(app, new RegExp(`window\\.${action}\\s*=`));
    assert.match(app, /workspace\.note_changed/);
});
