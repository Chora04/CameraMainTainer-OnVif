(function () {
    'use strict';

    const revisions = new WeakMap();
    const dirty = new WeakSet();
    document.addEventListener('input', markDirty);
    document.addEventListener('change', markDirty);
    function markDirty(event) {
        const form = event.target.closest('form, [data-live-editor]');
        if (!form) return;
        revisions.set(form, (revisions.get(form) || 0) + 1);
        dirty.add(form);
    }

    window.captureLiveForm = function (id) {
        const form = document.getElementById(id);
        return { form, revision: revisions.get(form) || 0 };
    };
    window.canApplyLiveForm = function (snapshot) {
        return Boolean(snapshot.form?.isConnected && !dirty.has(snapshot.form) &&
            snapshot.revision === (revisions.get(snapshot.form) || 0));
    };
    window.markLiveFormSaved = function (snapshot) {
        if (snapshot.form && snapshot.revision === (revisions.get(snapshot.form) || 0)) dirty.delete(snapshot.form);
    };

    window.refreshWorkspaceAccess = async function () {
        const initial = document.body.dataset.accessVersion;
        if (!initial) return true;
        try {
            const current = await apiFetch('/api/workspace/access');
            if (current.version === initial) return true;
            // Only a changed identity/permission set needs a new authorized page.
            document.querySelector('.page-content')?.replaceChildren();
            closeModal();
            window.location.reload();
            return false;
        } catch (error) {
            if (error.status === 401 || error.status === 403) {
                document.querySelector('.page-content')?.replaceChildren();
                closeModal();
            }
            return false;
        }
    };

    window.readLivePage = async function () {
        const response = await apiFetch(window.location.pathname + window.location.search, { cache: 'no-store' });
        return new DOMParser().parseFromString(await response.text(), 'text/html');
    };
})();
