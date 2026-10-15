(function () {
    'use strict';

    const originalFetch = window.fetch.bind(window);
    const pendingGets = new Map();
    const currentIntlLocale = () => window.uiLocale?.intlLocale || 'ru-RU';
    window.connection = { on: function () { }, start: function () { return Promise.reject(new Error('Соединение с сервером недоступно')); } };

    window.cameraErrorMessages = JSON.parse(document.getElementById('camera-error-messages')?.textContent || '{}');
    window.localizeCameraError = function (value) {
        const text = String(value ?? '');
        if (/^(?:timeout:|a task was cancel(?:ed|led)\.?$)/i.test(text.trim())) {
            return window.cameraErrorMessages.CameraCheckTimeout || text;
        }
        return text;
    };

    window.renderIcons = function () {
        if (!window.lucide) return;
        window.lucide.createIcons({ attrs: { 'aria-hidden': 'true', 'stroke-width': 1.8 } });
    };

    window.apiFetch = function (url, options) {
        const share = !options || ((!options.method || options.method === 'GET') && !options.signal && !options.headers);
        if (!share) return requestApi(url, options);
        if (!pendingGets.has(url)) {
            pendingGets.set(url, requestApi(url, options).finally(() => pendingGets.delete(url)));
        }
        return pendingGets.get(url).then(result => result && typeof result.clone === 'function' ? result.clone() : result);
    };

    async function requestApi(url, options) {
        const request = Object.assign({ credentials: 'same-origin' }, options || {});
        request.headers = Object.assign({ Accept: 'application/json' }, request.headers || {});
        if (request.body && typeof request.body !== 'string') {
            request.headers['Content-Type'] = 'application/json';
            request.body = JSON.stringify(request.body);
        }

        const response = await originalFetch(url, request);
        if (response.status === 401) {
            const returnUrl = window.location.pathname + window.location.search;
            if (!window.location.pathname.toLowerCase().startsWith('/login')) {
                window.location.href = '/Login?returnUrl=' + encodeURIComponent(returnUrl);
            }
            throw new Error('Сеанс истёк. Выполните вход снова.');
        }

        if (!response.ok) {
            let message = 'Операция не выполнена';
            try {
                const data = await response.clone().json();
                message = data.message || data.error || data.title || message;
            } catch (_) {
                const text = await response.clone().text();
                if (text) message = text.substring(0, 240);
            }
            const error = new Error(message);
            error.status = response.status;
            throw error;
        }

        if (response.status === 204) return null;
        const type = response.headers.get('content-type') || '';
        return type.includes('application/json') ? response.json() : response;
    }

    const lensClamp = value => Math.max(0, Math.min(1, value));
    const lensNumber = value => {
        if (value == null || value === '') return null;
        const number = Number(value);
        return Number.isFinite(number) ? number : null;
    };

    function assessLensMetrics(metrics) {
        const mean = lensNumber(metrics.mean);
        const entropy = lensNumber(metrics.entropy);
        const edges = lensNumber(metrics.edges);
        let stdDev = lensNumber(metrics.stdDev);
        const variance = lensNumber(metrics.variance);
        if (stdDev == null && variance != null && variance >= 0) stdDev = Math.sqrt(variance);

        if (mean == null || entropy == null || edges == null) {
            return { available: false, text: 'Нет данных для расчёта', evidence: 'Нужен последний кадр', tone: '' };
        }

        const darkness = metrics.darkRatio == null
            ? lensClamp((35 - mean) / 35)
            : lensClamp(Number(metrics.darkRatio));
        const overexposure = metrics.brightRatio == null
            ? lensClamp((mean - 220) / 35)
            : lensClamp(Number(metrics.brightRatio));
        const lowEdges = lensClamp((8 - edges) / 7);
        const lowEntropy = lensClamp((5 - entropy) / 4);
        const lowContrast = stdDev == null
            ? (lowEdges + lowEntropy) / 2
            : lensClamp((24 - stdDev) / 20);

        // Independent evidence fusion: illumination (55%) plus loss of detail (45%).
        const flatness = 0.40 * lowEdges + 0.35 * lowEntropy + 0.25 * lowContrast;
        const darkRisk = 0.55 * darkness + 0.45 * flatness;
        const brightRisk = 0.55 * overexposure + 0.45 * flatness;
        const flatRisk = 0.75 * flatness + 0.25 * (1 - Math.max(darkness, overexposure));
        const risk = lensClamp(Math.max(darkRisk, brightRisk, flatRisk));
        const percent = Math.round(risk * 100);

        let label = 'Норма';
        if (risk >= 0.72 && darkRisk >= brightRisk && darkness >= 0.55)
            label = 'Темно или объектив закрыт';
        else if (risk >= 0.72 && brightRisk > darkRisk && overexposure >= 0.55)
            label = 'Засветка или объектив перекрыт';
        else if (risk >= 0.72 && flatRisk >= darkRisk && flatRisk >= brightRisk)
            label = 'Возможна стена или поворот камеры';
        else if (risk >= 0.52)
            label = 'Требует визуальной проверки';

        const tone = risk >= 0.72 ? 'danger' : risk >= 0.52 ? 'warning' : 'ok';
        const evidence = 'Яркость: ' + mean.toFixed(1) + ' из 255' +
            ' · Контраст: ' + (stdDev == null ? 'нет данных' : stdDev.toFixed(1)) +
            ' · Энтропия: ' + entropy.toFixed(2) + ' бит' +
            ' · Доля контуров: ' + edges.toFixed(1) + '%';

        return {
            available: true,
            text: label + ' · риск ' + percent + '%',
            evidence,
            tone,
            risk,
            mean,
            stdDev,
            entropy,
            edges,
            darkRatio: darkness,
            brightRatio: overexposure
        };
    }

    function analyzeLensImage(image) {
        if (!image || !image.naturalWidth || !image.naturalHeight)
            throw new Error('Кадр не загружен');

        const width = Math.min(192, image.naturalWidth);
        const height = Math.max(3, Math.round(image.naturalHeight * width / image.naturalWidth));
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext('2d', { willReadFrequently: true });
        context.drawImage(image, 0, 0, width, height);
        const rgba = context.getImageData(0, 0, width, height).data;
        const gray = new Float32Array(width * height);
        const histogram = new Uint32Array(256);
        let sum = 0;
        let sumSquares = 0;
        let darkPixels = 0;
        let brightPixels = 0;

        for (let index = 0, pixel = 0; index < rgba.length; index += 4, pixel++) {
            const value = 0.2126 * rgba[index] + 0.7152 * rgba[index + 1] + 0.0722 * rgba[index + 2];
            gray[pixel] = value;
            const bin = Math.max(0, Math.min(255, Math.round(value)));
            histogram[bin]++;
            sum += value;
            sumSquares += value * value;
            if (value < 20) darkPixels++;
            if (value > 240) brightPixels++;
        }

        const count = gray.length;
        const mean = sum / count;
        const variance = Math.max(0, sumSquares / count - mean * mean);
        let entropy = 0;
        for (const binCount of histogram) {
            if (!binCount) continue;
            const probability = binCount / count;
            entropy -= probability * Math.log2(probability);
        }

        let edgePixels = 0;
        let testedPixels = 0;
        for (let y = 1; y < height - 1; y++) {
            for (let x = 1; x < width - 1; x++) {
                const top = (y - 1) * width + x;
                const middle = y * width + x;
                const bottom = (y + 1) * width + x;
                const gx = -gray[top - 1] + gray[top + 1] - 2 * gray[middle - 1] + 2 * gray[middle + 1] - gray[bottom - 1] + gray[bottom + 1];
                const gy = -gray[top - 1] - 2 * gray[top] - gray[top + 1] + gray[bottom - 1] + 2 * gray[bottom] + gray[bottom + 1];
                if (Math.hypot(gx, gy) > 110) edgePixels++;
                testedPixels++;
            }
        }

        return assessLensMetrics({
            mean,
            variance,
            entropy,
            edges: testedPixels ? edgePixels / testedPixels * 100 : 0,
            darkRatio: darkPixels / count,
            brightRatio: brightPixels / count
        });
    }

    window.CameraLensMath = {
        assess: assessLensMetrics,
        analyzeImage: analyzeLensImage
    };

    window.escapeHtml = function (value) {
        const div = document.createElement('div');
        div.textContent = value == null ? '' : String(value);
        return div.innerHTML;
    };

    window.showToast = function (message, type) {
        const container = document.getElementById('toast-container');
        if (!container) return;
        const toast = document.createElement('div');
        toast.className = 'toast toast-' + (type || 'info');
        const toastIcon = type === 'error' ? 'circle-alert' : type === 'success' ? 'circle-check' : 'info';
        toast.innerHTML = '<span class="toast-mark"><i data-lucide="' + toastIcon + '"></i></span>'
            + '<span class="toast-body">' + escapeHtml(message) + '</span>'
            + '<button class="toast-close" type="button" aria-label="Закрыть"><i data-lucide="x"></i></button>';
        toast.querySelector('.toast-close').onclick = () => toast.remove();
        container.appendChild(toast);
        renderIcons();
        setTimeout(() => {
            toast.classList.add('exiting');
            setTimeout(() => toast.remove(), 220);
        }, 5000);
    };

    window.renderHandoffAudit = function (handoff) {
        if (!handoff) return '';
        const states = { pending: 'Ожидает принятия', accepted: 'Принято', forwarded: 'Передано дальше', completed: 'Завершено', rejected: 'Возвращено' };
        const fields = [
            ['Передал', handoff.fromDisplayName || handoff.fromUsername],
            ['Получатель', handoff.toDisplayName || handoff.toUsername],
            ['Причина', handoff.reasonName],
            ['Состояние передачи', states[handoff.state] || handoff.state],
            ['Комментарий', handoff.comment],
            ['При принятии', handoff.acceptanceComment],
            ['Результат', handoff.completionComment],
            ['Срок', handoff.dueAt && formatTashkentDateTime(handoff.dueAt)]
        ];
        return '<div class="error-audit-changes">' + fields.filter(([, value]) => value).map(([label, value]) =>
            '<div class="error-audit-change"><span>' + escapeHtml(label) + '</span><div><strong>' + escapeHtml(value) + '</strong></div></div>'
        ).join('') + '</div>';
    };

    window.showModal = function (title, bodyHtml, footerHtml, legacyAction) {
        const container = document.getElementById('modal-container');
        if (!container) return;
        const context = document.getElementById('modal-context');
        if (context) {
            context.className = 'modal-context';
            context.innerHTML = '<span class="eyebrow">Действие</span>';
        }
        const subcontext = document.getElementById('modal-subcontext');
        if (subcontext) {
            subcontext.className = 'modal-subcontext';
            subcontext.innerHTML = '';
            subcontext.hidden = true;
        }
        const titleBadges = document.getElementById('modal-title-badges');
        if (titleBadges) {
            titleBadges.innerHTML = '';
            titleBadges.hidden = true;
        }
        const headerSeverity = document.getElementById('modal-header-severity');
        if (headerSeverity) {
            headerSeverity.innerHTML = '';
            headerSeverity.hidden = true;
        }
        const serial = document.getElementById('modal-alert-serial');
        if (serial) {
            serial.textContent = '';
            serial.hidden = true;
        }
        document.getElementById('modal-title').textContent = title || '';
        document.getElementById('modal-body').innerHTML = bodyHtml || '';
        const footer = document.getElementById('modal-footer');
        if (typeof legacyAction === 'function') {
            footer.innerHTML = '<button class="btn btn-secondary" type="button" onclick="closeModal()">Отмена</button><button class="btn btn-primary" type="button" id="legacy-modal-action">' + escapeHtml(footerHtml || 'Подтвердить') + '</button>';
            document.getElementById('legacy-modal-action').onclick = function () { closeModal(); legacyAction(); };
        } else {
            footer.innerHTML = footerHtml || '';
        }
        container.hidden = false;
        document.body.classList.add('modal-open');
        renderIcons();
    };

    window.apiPost = function (url, body) {
        return apiFetch(url, { method: 'POST', body: body || {} });
    };

    window.closeModal = function (options) {
        const container = document.getElementById('modal-container');
        if (container) container.hidden = true;
        const pageModal = document.getElementById('camera-editor');
        if (pageModal) pageModal.hidden = true;
        document.body.classList.remove('modal-open');
        if (options?.notify !== false) document.dispatchEvent(new CustomEvent('camera-maintainer:modal-closed'));
    };

    window.confirmDialog = function (title, message, onConfirm) {
        showModal(title, '<p class="modal-message">' + escapeHtml(message) + '</p>',
            '<button class="btn btn-secondary" type="button" onclick="closeModal()">Отмена</button>'
            + '<button class="btn btn-danger" type="button" id="modal-confirm-btn">Подтвердить</button>');
        document.getElementById('modal-confirm-btn').onclick = function () {
            closeModal();
            if (typeof onConfirm === 'function') onConfirm();
        };
    };

    window.toggleSidebar = function (open) {
        const sidebar = document.getElementById('sidebar');
        const backdrop = document.getElementById('sidebar-backdrop');
        const toggle = document.getElementById('sidebar-toggle');
        if (!sidebar) return;

        const isMobile = window.matchMedia('(max-width: 700px)').matches;
        if (isMobile) {
            const mobileOpen = sidebar.classList.contains('mobile-open');
            const shouldOpenMobile = typeof open === 'boolean' ? open : !mobileOpen;
            sidebar.classList.toggle('mobile-open', shouldOpenMobile);
            if (backdrop) backdrop.classList.toggle('visible', shouldOpenMobile);
            if (toggle) toggle.setAttribute('aria-expanded', shouldOpenMobile ? 'true' : 'false');
            return;
        }

        const isOpen = !document.body.classList.contains('sidebar-collapsed');
        const shouldOpen = typeof open === 'boolean' ? open : !isOpen;

        sidebar.classList.remove('mobile-open');
        if (backdrop) backdrop.classList.remove('visible');
        document.body.classList.toggle('sidebar-collapsed', !shouldOpen);
        localStorage.setItem('cm_sidebar_collapsed', shouldOpen ? '0' : '1');
        if (!shouldOpen) {
            sidebar.querySelectorAll('.nav-group.open').forEach(group => {
                group.classList.remove('open');
                const groupToggle = group.querySelector('.nav-group-toggle');
                if (groupToggle) groupToggle.setAttribute('aria-expanded', 'false');
            });
        }

        if (toggle) toggle.setAttribute('aria-expanded', shouldOpen ? 'true' : 'false');
    };

    window.toggleSidebarCollapse = function () {
        toggleSidebar();
    };

    window.toggleNavGroup = function (button) {
        const group = button ? button.closest('.nav-group') : null;
        if (!group) return;

        // The mobile-specific condition is temporarily disabled.
        if (document.body.classList.contains('sidebar-collapsed')) {
            toggleSidebar(true);
        }

        const open = !group.classList.contains('open');
        group.classList.toggle('open', open);
        if (button) button.setAttribute('aria-expanded', open ? 'true' : 'false');
    };

    window.toggleAlertMenu = function (button) {
        toggleNavGroup(button);
    };

    window.logout = async function () {
        try { await originalFetch('/api/accounts/logout', { method: 'POST', credentials: 'same-origin' }); } catch (_) { }
        window.location.href = '/Login';
    };

    window.openImageViewer = function (image) {
        const viewer = document.getElementById('image-viewer');
        const target = document.getElementById('image-viewer-img');
        if (!viewer || !target || !image || !image.src) return;
        target.src = image.currentSrc || image.src;
        document.getElementById('image-viewer-caption').textContent = image.alt || 'Кадр ONVIF';
        viewer.hidden = false;
        document.body.classList.add('modal-open');
    };

    window.closeImageViewer = function () {
        const viewer = document.getElementById('image-viewer');
        if (viewer) viewer.hidden = true;
        document.body.classList.remove('modal-open');
    };

    window.checkNow = async function (id, button) {
        const original = button && button.innerHTML;
        if (button) { button.disabled = true; button.innerHTML = 'Проверка...'; }
        try {
            await apiFetch('/api/cameras/' + id + '/health/check', { method: 'POST' });
            window.refreshVisibleData('manual-check', ['cameras'], [Number(id)]);
            showToast('Проверка камеры завершена', 'success');
        } catch (error) {
            showToast(error.message, 'error');
        } finally {
            if (button) { button.disabled = false; button.innerHTML = original; }
        }
    };

    function applyHealthSummary(summary) {
        if (!summary) return;
        updateSummaryValue('total', summary.total);
        updateSummaryValue('online', summary.online);
        updateSummaryValue('offline', summary.offline);
        updateSummaryValue('attention', Number(summary.suspect || 0) + Number(summary.unknown || 0));
    }

    async function refreshCameraSummary(detail) {
        if (!detail.topics.includes('cameras') || !document.querySelector('[data-summary-value]')) return;
        applyHealthSummary(await apiFetch('/api/health/status'));
    }

    function updateSummaryValue(name, value) {
        const element = document.querySelector('[data-summary-value="' + name + '"]');
        if (element && value !== undefined && value !== null) element.textContent = String(value);
    }

    function formatTashkent(date) {
        try {
            return new Intl.DateTimeFormat(currentIntlLocale(), {
                timeZone: 'Asia/Tashkent', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false
            }).format(date);
        } catch (_) {
            return new Date(date.getTime() + 5 * 60 * 60 * 1000).toISOString().substring(11, 19);
        }
    }

    function formatTashkentDate(date) {
        try {
            return new Intl.DateTimeFormat(currentIntlLocale(), {
                timeZone: 'Asia/Tashkent', day: '2-digit', month: 'short', year: 'numeric'
            }).format(date);
        } catch (_) {
            return date.toISOString().substring(0, 10);
        }
    }

    function formatTashkentDateTime(value) {
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return '—';
        try {
            return new Intl.DateTimeFormat(currentIntlLocale(), {
                timeZone: 'Asia/Tashkent', day: '2-digit', month: '2-digit', year: 'numeric',
                hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false
            }).format(date).replace(',', '');
        } catch (_) {
            return date.toLocaleString(currentIntlLocale());
        }
    }

    window.formatTashkentDateTime = formatTashkentDateTime;

    const cameraMessage = (key, fallback) => window.cameraErrorMessages?.[key] || fallback;
    const cameraFailureReasons = {
        timeout: cameraMessage('FailureReasonTimeout', 'Камера не ответила за отведённое время'),
        connection_refused: cameraMessage('FailureReasonConnectionRefused', 'Соединение с камерой отклонено'),
        auth_failed: cameraMessage('FailureReasonAuthFailed', 'Камера отклонила логин или пароль'),
        not_found: cameraMessage('FailureReasonNotFound', 'ONVIF-служба камеры не найдена'),
        http_error: cameraMessage('FailureReasonHttpError', 'Камера вернула ошибку HTTP'),
        soap_fault: cameraMessage('FailureReasonSoapFault', 'Камера вернула ошибку ONVIF'),
        no_credentials: cameraMessage('FailureReasonNoCredentials', 'Для камеры не указаны данные доступа'),
        credentials_invalid: cameraMessage('FailureReasonCredentialsInvalid', 'Сохранённые данные доступа повреждены'),
        circuit_open: cameraMessage('FailureReasonCircuitOpen', 'Проверки временно приостановлены после повторных сбоев'),
        unknown_error: cameraMessage('FailureReasonUnknown', 'Проверка завершилась с неизвестной ошибкой')
    };
    const cameraFailureStages = {
        network_probe: cameraMessage('FailureStageNetworkProbe', 'Проверка доступности сети'),
        onvif_device_info: cameraMessage('FailureStageDeviceInfo', 'Получение основных данных ONVIF'),
        processing_camera_data: cameraMessage('FailureStageProcessing', 'Обработка ответа камеры'),
        firmware_history: cameraMessage('FailureStageFirmware', 'Сохранение сведений о прошивке'),
        camera_metadata_save: cameraMessage('FailureStageMetadata', 'Сохранение данных камеры'),
        camera_status_save: cameraMessage('FailureStageStatus', 'Обновление состояния камеры'),
        alert_evaluation: cameraMessage('FailureStageAlerts', 'Обработка результатов диагностики'),
        health_result_save: cameraMessage('FailureStageHealth', 'Сохранение результата проверки')
    };

    function inferCameraFailureCode(issue) {
        if (issue?.failureCode) return String(issue.failureCode).toLowerCase();
        const text = String(issue?.description || '').trim();
        const prefix = /^([a-z_]+):/i.exec(text)?.[1]?.toLowerCase();
        if (prefix && cameraFailureReasons[prefix]) return prefix;
        if (/^(?:timeout:|a task was cancel(?:ed|led)\.?$)/i.test(text)) return 'timeout';
        return '';
    }

    function formatDiagnosticDuration(value) {
        if (value === null || value === undefined || value === '') return '';
        const milliseconds = Number(value);
        if (!Number.isFinite(milliseconds) || milliseconds < 0) return '';
        if (milliseconds < 1000) return Math.round(milliseconds) + ' мс';
        return (milliseconds / 1000).toLocaleString(currentIntlLocale(), { maximumFractionDigits: 1 }) + ' сек';
    }

    window.getCameraIssueDiagnosis = function (issue) {
        if (!issue) return null;
        const code = inferCameraFailureCode(issue);
        const hasDuration = issue.failureDurationMs !== null && issue.failureDurationMs !== undefined &&
            Number.isFinite(Number(issue.failureDurationMs));
        const hasStoredDiagnostic = Boolean(issue.failureStage) || hasDuration;
        if (!code && !hasStoredDiagnostic) return null;

        const duration = formatDiagnosticDuration(issue.failureDurationMs);
        const timeout = formatDiagnosticDuration(issue.failureTimeoutMs);
        let timing = duration || cameraMessage('DiagnosisTimeMissing', 'Точное время не сохранено');
        if (duration && timeout) timing = cameraMessage('DiagnosisTimingWithLimit', '{0} при лимите {1}')
            .replace('{0}', duration).replace('{1}', timeout);
        else if (!duration && code === 'timeout') timing = cameraMessage('DiagnosisLegacyTimeout', 'Лимит ожидания превышен; точное время в старой записи не сохранено');

        return {
            code,
            reason: cameraFailureReasons[code] || window.localizeCameraError(issue.description) || 'Неизвестная ошибка проверки',
            stage: cameraFailureStages[issue.failureStage] || (issue.failureStage
                ? cameraMessage('DiagnosisInternalStage', 'Служебный этап: {0}').replace('{0}', issue.failureStage)
                : cameraMessage('DiagnosisLegacyStage', 'Не зафиксирован в старой записи')),
            timing,
            recovered: Boolean(issue.recoveredAt),
            result: issue.recoveredAt
                ? cameraMessage('DiagnosisRecovered', 'Следующая проверка успешна: {0}. Камера снова отвечает.')
                    .replace('{0}', formatTashkentDateTime(issue.recoveredAt))
                : cameraMessage('DiagnosisActive', 'Ошибка остаётся активной; успешная повторная проверка пока не зафиксирована.')
        };
    };

    window.renderCameraIssueDiagnosis = function (diagnosis) {
        if (!diagnosis) return '';
        const row = (label, value, css) => '<div' + (css ? ' class="' + css + '"' : '') + '><dt>' +
            window.escapeHtml(label) + ':</dt><dd>' + window.escapeHtml(value) + '</dd></div>';
        return '<dl class="alert-issue-diagnosis">' +
            row(cameraMessage('DiagnosisReasonLabel', 'Причина'), diagnosis.reason, 'primary') +
            row(cameraMessage('DiagnosisStageLabel', 'Этап проверки'), diagnosis.stage) +
            row(cameraMessage('DiagnosisTimingLabel', 'Время проверки'), diagnosis.timing) +
            row(cameraMessage('DiagnosisNextCheckLabel', 'Результат следующей проверки'), diagnosis.result, diagnosis.recovered ? 'success' : 'pending') +
            '</dl>';
    };

    window.renderCameraCaseIssues = function (issues, options) {
        if (!Array.isArray(issues) || !issues.length) return '';
        options = options || {};
        const names = ['CameraOffline', 'TemperatureHigh', 'FirmwareOutdated', 'AuthenticationFailed', 'ConfigurationChanged', 'AutoRecoveryFailed', 'LensObstructed'];
        const titles = ['Камера недоступна', 'Повышенная температура камеры', 'Требуется обновление прошивки', 'Ошибка авторизации камеры', 'Изменились настройки камеры', 'Автоматическое восстановление не выполнено', 'Проблема с объективом камеры'];
        return '<ul class="alert-issue-list">' + issues.map(function (issue, issueIndex) {
            const index = typeof issue.alertType === 'number' ? issue.alertType : names.indexOf(issue.alertType);
            const title = /[\u0400-\u04ff]/.test(issue.title || '') ? issue.title : titles[index] || issue.title || 'Ошибка камеры';
            const diagnosis = window.getCameraIssueDiagnosis(issue);
            const messages = Array.isArray(issue.messages) ? issue.messages.slice().sort((left, right) => new Date(left.createdAt) - new Date(right.createdAt)) : [];
            const legacy = issueIndex === 0 && options.legacyReport ? {
                message: options.legacyReport,
                authorDisplayName: options.legacyAuthor || 'Ранее сохранённый отчёт',
                createdAt: options.legacyCreatedAt || issue.startedAt,
                legacy: true
            } : null;
            const reportMessages = legacy ? [legacy].concat(messages) : messages;
            const issueId = Number(issue.id);
            const alertId = Number(options.alertId);
            const canWrite = options.canEdit === true && Number.isSafeInteger(issueId) && issueId > 0 && Number.isSafeInteger(alertId) && alertId > 0;
            const fixedBy = issue.resolvedByDisplayName || issue.resolvedByUsername || 'Не указано';
            const chat = '<section class="alert-issue-chat"><header><h4><i data-lucide="messages-square"></i> Отчёт по работе:</h4><span>' + reportMessages.length + '</span></header>' +
                '<div class="alert-issue-chat-list">' + (reportMessages.length ? reportMessages.map(message =>
                    '<article' + (message.legacy ? ' class="legacy"' : '') + '><header><strong>' + window.escapeHtml(message.authorDisplayName || message.authorUsername || 'Пользователь') +
                    '</strong><time>' + window.escapeHtml(formatTashkentDateTime(message.createdAt)) + '</time></header><p>' + window.escapeHtml(message.message || '') + '</p></article>').join('') :
                    '<span class="alert-issue-chat-empty">Сообщений пока нет</span>') + '</div>' +
                (canWrite ? '<form class="alert-issue-chat-form" data-preserve-on-refresh onsubmit="submitAlertIssueMessage(event,' + alertId + ',' + issueId + ')"><textarea class="form-input" rows="2" maxlength="4000" required placeholder="Что проверили или исправили"></textarea><button class="btn btn-primary" type="submit" title="Отправить сообщение"><i data-lucide="send"></i><span>Отправить</span></button></form>' : '') + '</section>';
            return '<li class="' + (issue.recoveredAt ? 'recovered' : '') + '"><div class="alert-issue-title"><strong>' + window.escapeHtml(diagnosis?.reason || title) + '</strong><span>' +
                (issue.category === 'Physical' || issue.category === 1 ? 'Физическая' : 'Техническая') + '</span></div>' +
                '<dl class="alert-issue-lifecycle"><div><dt>Начало:</dt><dd>' + window.escapeHtml(formatTashkentDateTime(issue.startedAt)) + '</dd></div>' +
                '<div><dt>Конец:</dt><dd>' + (issue.recoveredAt ? window.escapeHtml(formatTashkentDateTime(issue.recoveredAt)) : 'Ошибка активна') + '</dd></div>' +
                '<div><dt>Исправил:</dt><dd>' + window.escapeHtml(fixedBy) + '</dd></div></dl>' +
                window.renderCameraIssueDiagnosis(diagnosis) +
                (issue.description && !diagnosis ? '<p>' + window.escapeHtml(window.localizeCameraError(issue.description)) + '</p>' : '') + chat + '</li>';
        }).join('') + '</ul>';
    };

    function startTrustedClock() {
        const clock = document.getElementById('tashkent-clock');
        const date = document.getElementById('tashkent-date');
        let serverNow = Date.now();
        let capturedAt = performance.now();

        function paint() {
            const now = new Date(serverNow + (performance.now() - capturedAt));
            if (clock) clock.textContent = formatTashkent(now);
            if (date && !date.dataset.source) date.textContent = formatTashkentDate(now) + ' · UTC+5';
            setTimeout(paint, 1000);
        }
        paint();

        originalFetch('/api/time/tashkent', { credentials: 'same-origin', cache: 'no-store' })
            .then(response => response.ok ? response.json() : null)
            .then(data => {
                if (!data) return;
                const parsed = Date.parse(data.tashkentTime || data.utcTime);
                if (!Number.isNaN(parsed)) {
                    serverNow = parsed;
                    capturedAt = performance.now();
                }
                if (date) {
                    date.dataset.source = data.source || 'server';
                    date.textContent = formatTashkentDate(new Date(serverNow)) + ' · ' + (data.source === 'ntp' ? 'NTP' : 'сервер');
                }
            })
            .catch(() => { if (date) date.textContent = 'серверное время · UTC+5'; });
    }

    function startSignalR() {
        if (!window.signalR) return;
        const connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/events')
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .configureLogging(signalR.LogLevel.Warning)
            .build();
        const pill = document.getElementById('live-pill');
        const label = document.getElementById('live-label');
        const setState = (state, text) => {
            if (pill) {
                pill.className = 'connection-pill ' + state;
                pill.title = text;
                pill.setAttribute('aria-label', text);
                const dot = pill.querySelector('.status-dot');
                if (dot) dot.className = 'status-dot ' + state;
            }
            if (label) label.textContent = text;
        };
        let retryTimer = null;
        const connected = () => {
            setState('online', 'Онлайн');
            window.refreshVisibleData('connected');
        };
        const retry = () => {
            if (retryTimer !== null) return;
            retryTimer = window.setTimeout(() => { retryTimer = null; connect(); }, 5000);
        };
        const connect = () => {
            if (connection.state !== signalR.HubConnectionState.Disconnected) return;
            connection.start().then(connected).catch(() => {
                setState('offline', 'Офлайн');
                retry();
            });
        };
        connection.onreconnecting(() => setState('offline', 'Офлайн'));
        connection.onreconnected(connected);
        connection.onclose(() => { setState('offline', 'Офлайн'); retry(); });
        const cameraChanged = data => {
            const cameraIds = Array.isArray(data?.cameraIds)
                ? data.cameraIds.map(Number).filter(id => Number.isInteger(id) && id > 0)
                : data && Number(data.cameraId) > 0 ? [Number(data.cameraId)] : null;
            window.refreshVisibleData('signalr', ['cameras'], cameraIds?.length ? cameraIds : null);
        };
        const alertsChanged = () => window.refreshVisibleData('signalr', ['alerts', 'notifications', 'navigation']);
        connection.on('health.updated', cameraChanged);
        connection.on('camera.status_changed', cameraChanged);
        connection.on('camera.found', () => window.refreshVisibleData('signalr'));
        ['alerts.changed', 'alert.created', 'alert.resolved', 'alert.handoff_changed'].forEach(name => {
            connection.on(name, alertsChanged);
        });
        connection.on('notification.changed', () => window.refreshVisibleData('signalr', ['notifications']));
        connection.on('workspace.note_changed', () => {
            window.refreshVisibleData('signalr', ['notifications']);
            const popover = document.getElementById('workspace-planner-popover');
            if (popover && !popover.hidden) loadWorkspaceNotes().catch(() => { });
        });
        connection.on('workspace.changed', payload => {
            if (Array.isArray(payload?.topics)) window.refreshVisibleData('signalr', payload.topics);
        });
        window.connection = connection;
        connect();
    }

    let accountNotificationItems = [];
    let accountNotificationTab = 'new';
    let accountNotificationsLoaded = false;
    let notificationSoundEnabled = true;
    let notificationSoundPending = false;
    let notificationAudio = null;
    const notificationAnnouncements = new Map();

    async function unlockNotificationAudio() {
        if (!notificationSoundEnabled) return;
        try {
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            if (!AudioContext) return;
            notificationAudio ??= new AudioContext();
            if (notificationAudio.state === 'suspended') await notificationAudio.resume();
            if (notificationSoundPending && notificationAudio.state === 'running') playNotificationSound();
        } catch (_) { /* Audio may remain blocked until the browser accepts a user gesture. */ }
    }

    function playNotificationSound() {
        if (!notificationSoundEnabled) return;
        if (!notificationAudio || notificationAudio.state !== 'running') {
            notificationSoundPending = true;
            return;
        }
        notificationSoundPending = false;
        const start = notificationAudio.currentTime;
        [660, 880].forEach(function (frequency, index) {
            const oscillator = notificationAudio.createOscillator();
            const gain = notificationAudio.createGain();
            const at = start + index * .17;
            oscillator.type = 'sine';
            oscillator.frequency.value = frequency;
            gain.gain.setValueAtTime(0, at);
            gain.gain.linearRampToValueAtTime(.1, at + .015);
            gain.gain.exponentialRampToValueAtTime(.001, at + .16);
            oscillator.connect(gain);
            gain.connect(notificationAudio.destination);
            oscillator.start(at);
            oscillator.stop(at + .17);
            oscillator.onended = () => { oscillator.disconnect(); gain.disconnect(); };
        });
    }

    document.addEventListener('pointerdown', unlockNotificationAudio);
    document.addEventListener('keydown', unlockNotificationAudio);

    window.testNotificationSound = async function () {
        const enabled = notificationSoundEnabled;
        notificationSoundEnabled = true;
        notificationSoundPending = true;
        try { await unlockNotificationAudio(); } finally { notificationSoundEnabled = enabled; }
    };

    function renderNotificationAnnouncement() {
        let container = document.getElementById('notification-announcement');
        const item = notificationAnnouncements.values().next().value;
        if (!item) { container?.remove(); return; }
        if (!container) {
            container = document.createElement('section');
            container.id = 'notification-announcement';
            container.className = 'notification-announcement';
            container.setAttribute('role', 'status');
            container.setAttribute('aria-live', 'polite');
            document.body.appendChild(container);
        }
        const remaining = notificationAnnouncements.size - 1;
        container.innerHTML = '<span class="notification-announcement-icon" aria-hidden="true"><i data-lucide="bell-ring"></i></span>' +
            '<a class="notification-announcement-link" href="' + escapeHtml(item.url || '/Alerts?view=active') + '" data-notification-id="' + escapeHtml(item.notificationId) + '">' +
                '<strong>' + escapeHtml(item.title) + '</strong><span class="notification-announcement-message">' + escapeHtml(item.message) + '</span>' +
                '<span class="notification-announcement-meta"><time>' + escapeHtml(formatTashkentDateTime(item.occurredAt)) + '</time>' +
                    (remaining > 0 ? '<span>Ещё ' + remaining + '</span>' : '') + '</span></a>' +
            '<button type="button" class="icon-button notification-announcement-dismiss" title="Отметить просмотренным" aria-label="Отметить просмотренным"><i data-lucide="x"></i></button>';
        container.querySelector('a').onclick = event => openAccountNotification(event, event.currentTarget);
        container.querySelector('button').onclick = async function () {
            this.disabled = true;
            try {
                await apiPost('/api/notifications/read', { notificationId: item.notificationId });
                notificationAnnouncements.delete(item.notificationId);
                renderNotificationAnnouncement();
                await loadAccountNotifications(false);
            } catch (error) { this.disabled = false; showToast(error.message, 'error'); }
        };
        renderIcons();
    }

    window.toggleAccountNotifications = function (event) {
        if (event) event.stopPropagation();
        const popover = document.getElementById('notification-popover');
        const button = document.querySelector('.notification-button');
        if (!popover || !button) return;
        const shouldOpen = popover.hidden;
        if (shouldOpen) {
            closeSystemMetrics();
            closeWorkspacePlanner();
        }
        popover.hidden = !shouldOpen;
        button.setAttribute('aria-expanded', shouldOpen ? 'true' : 'false');
        if (shouldOpen) loadAccountNotifications().catch(() => { });
    };

    async function loadAccountNotifications(announceNew) {
        const list = document.getElementById('notification-list');
        const badge = document.getElementById('notification-count');
        const readBadge = document.getElementById('notification-read-badge');
        const summary = document.getElementById('notification-summary');
        if (!list || !badge || !readBadge || !summary) return;

        const feed = await apiFetch('/api/notifications');
        notificationSoundEnabled = feed.soundEnabled !== false;
        if (!notificationSoundEnabled) notificationSoundPending = false;
        const items = Array.isArray(feed.items) ? feed.items : [];
        const count = Number(feed.count || 0);
        const readCount = items.filter(item => item.isRead).length;
        const noteCount = items.filter(item => item.type === 'calendar_note' || item.type === 'calendar_overdue').length;
        const plannerBadge = document.getElementById('planner-note-count');
        if (plannerBadge) {
            plannerBadge.textContent = noteCount > 99 ? '99+' : String(noteCount);
            plannerBadge.hidden = noteCount === 0;
        }
        const previousUnreadIds = new Set(accountNotificationItems
            .filter(item => !item.isRead)
            .map(item => item.notificationId));
        const arrived = items.filter(item => !item.isRead && !previousUnreadIds.has(item.notificationId));
        accountNotificationItems = items;
        badge.textContent = count > 99 ? '99+' : String(count);
        badge.hidden = count === 0;
        readBadge.textContent = readCount > 99 ? '99+' : String(readCount);
        readBadge.hidden = readCount === 0;
        summary.textContent = count === 0 ? 'Новых уведомлений нет' : 'Новых уведомлений: ' + count;
        renderAccountNotificationList();
        const unreadIds = new Set(items.filter(item => !item.isRead).map(item => item.notificationId));
        for (const id of notificationAnnouncements.keys()) {
            if (!unreadIds.has(id)) notificationAnnouncements.delete(id);
        }
        if (feed.popupEnabled === false) notificationAnnouncements.clear();
        else if (announceNew || !accountNotificationsLoaded)
            arrived.forEach(item => notificationAnnouncements.set(item.notificationId, item));
        renderNotificationAnnouncement();
        if ((announceNew || !accountNotificationsLoaded) && arrived.length > 0) playNotificationSound();
        accountNotificationsLoaded = true;
    }

    function renderAccountNotificationList() {
        const list = document.getElementById('notification-list');
        const newCount = document.getElementById('notification-new-count');
        const readCount = document.getElementById('notification-read-count');
        if (!list || !newCount || !readCount) return;

        const unreadItems = accountNotificationItems.filter(item => !item.isRead);
        const readItems = accountNotificationItems.filter(item => item.isRead);
        if (accountNotificationTab === 'new' && unreadItems.length === 0 && readItems.length > 0)
            accountNotificationTab = 'read';
        newCount.textContent = String(unreadItems.length);
        readCount.textContent = String(readItems.length);
        document.querySelectorAll('[data-notification-tab]').forEach(button => {
            const active = button.dataset.notificationTab === accountNotificationTab;
            button.classList.toggle('active', active);
            button.setAttribute('aria-selected', active ? 'true' : 'false');
        });

        const items = accountNotificationTab === 'read' ? readItems : unreadItems;

        if (!items.length) {
            const isReadTab = accountNotificationTab === 'read';
            list.innerHTML = '<span class="notification-empty"><i data-lucide="' + (isReadTab ? 'history' : 'check-circle-2') + '"></i><strong>' + (isReadTab ? 'Просмотренных пока нет' : 'Новых уведомлений нет') + '</strong><small>' + (isReadTab ? 'Открытые уведомления появятся здесь' : 'Новых назначений и просроченных карточек нет') + '</small></span>';
            renderIcons();
            return;
        }

        const icons = {
            overdue: 'clock-alert',
            review: 'clipboard-check',
            assignment: 'user-check',
            new: 'circle-alert',
            handoff_received: 'send',
            handoff_completed: 'badge-check',
            handoff_rejected: 'undo-2',
            handoff_overdue: 'alarm-clock',
            calendar_note: 'calendar-clock',
            calendar_overdue: 'calendar-x-2'
        };
        list.innerHTML = items.map(item => {
            const occurredAt = formatTashkentDateTime(item.occurredAt);
            const address = item.address ? '<code>' + escapeHtml(item.address) + '</code>' : '';
            return '<a class="notification-item ' + escapeHtml(item.tone || '') + (item.isRead ? ' is-read' : '') + '" href="' + escapeHtml(item.url || '/Alerts?view=active') + '" data-notification-id="' + escapeHtml(item.notificationId || '') + '" onclick="openAccountNotification(event, this)">' +
                '<span class="notification-item-icon"><i data-lucide="' + (icons[item.type] || 'bell') + '"></i></span>' +
                '<span><strong>' + escapeHtml(item.title || 'Уведомление') + '</strong><small>' + escapeHtml(item.message || '') + '</small><time>' + escapeHtml(occurredAt) + address + '</time></span>' +
            '</a>';
        }).join('');
        renderIcons();
    }

    window.switchAccountNotificationTab = function (tab, event) {
        if (event) event.stopPropagation();
        accountNotificationTab = tab === 'read' ? 'read' : 'new';
        renderAccountNotificationList();
    };

    window.openAccountNotification = async function (event, link) {
        event.preventDefault();
        const notificationId = link.dataset.notificationId;
        const target = link.getAttribute('href') || '/Alerts?view=active';
        try {
            if (notificationId)
                await apiPost('/api/notifications/read', { notificationId });
            notificationAnnouncements.delete(notificationId);
            renderNotificationAnnouncement();
        } catch (_) {
            // Opening the related card remains available if read-state persistence fails.
        } finally {
            window.location.href = target;
        }
    };
    window.loadAccountNotifications = loadAccountNotifications;

    const workspaceMonthNames = new Intl.DateTimeFormat(currentIntlLocale(), {
        timeZone: 'UTC', month: 'long', year: 'numeric'
    });
    let workspaceNotes = [];
    let workspaceCalendarMonth = '';
    let workspaceSelectedDate = '';
    let workspaceNotesLoading = false;
    let workspaceNoteRecipients = [];
    let workspaceCanShareNotes = document.getElementById('workspace-planner')?.dataset.canShare === 'true';

    function syncWorkspaceNoteAudienceOptions(selectedValue, selectedNote) {
        const select = document.getElementById('workspace-note-audience');
        if (!select || !workspaceCanShareNotes) return;
        const requestedValue = selectedValue || select.value || 'private';
        select.replaceChildren();
        const addOption = (value, label) => {
            const option = document.createElement('option');
            option.value = value;
            option.textContent = label;
            select.appendChild(option);
        };
        addOption('private', 'Только мне');
        addOption('all', 'Всем аккаунтам');
        workspaceNoteRecipients.forEach(recipient => {
            const name = recipient.displayName || recipient.username || 'Пользователь';
            const login = recipient.username && recipient.username !== name ? ' (' + recipient.username + ')' : '';
            addOption('user:' + recipient.id, name + login);
        });
        if (selectedNote?.recipientUserId &&
            !workspaceNoteRecipients.some(recipient => Number(recipient.id) === Number(selectedNote.recipientUserId))) {
            addOption(
                'user:' + selectedNote.recipientUserId,
                (selectedNote.recipientDisplayName || selectedNote.recipientUsername || 'Получатель') + ' (недоступен)');
        }
        select.value = Array.from(select.options).some(option => option.value === requestedValue)
            ? requestedValue
            : 'private';
    }

    function tashkentDateKey(value) {
        const date = value ? new Date(value) : new Date();
        if (Number.isNaN(date.getTime())) return '';
        const parts = new Intl.DateTimeFormat('en-CA', {
            timeZone: 'Asia/Tashkent', year: 'numeric', month: '2-digit', day: '2-digit'
        }).formatToParts(date).reduce((result, part) => {
            if (part.type !== 'literal') result[part.type] = part.value;
            return result;
        }, {});
        return parts.year + '-' + parts.month + '-' + parts.day;
    }

    function calendarKey(year, month, day) {
        return String(year).padStart(4, '0') + '-' + String(month).padStart(2, '0') + '-' + String(day).padStart(2, '0');
    }

    function addCalendarDays(key, amount) {
        const [year, month, day] = key.split('-').map(Number);
        const date = new Date(Date.UTC(year, month - 1, day + amount));
        return calendarKey(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate());
    }

    function calendarGridStart(monthKey) {
        const [year, month] = monthKey.split('-').map(Number);
        const first = new Date(Date.UTC(year, month - 1, 1));
        const mondayOffset = (first.getUTCDay() + 6) % 7;
        return addCalendarDays(calendarKey(year, month, 1), -mondayOffset);
    }

    function noteTime(value) {
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return '—';
        return new Intl.DateTimeFormat(currentIntlLocale(), {
            timeZone: 'Asia/Tashkent', hour: '2-digit', minute: '2-digit', hour12: false
        }).format(date);
    }

    function selectedDateTitle(key) {
        const [year, month, day] = key.split('-').map(Number);
        const date = new Date(Date.UTC(year, month - 1, day));
        return new Intl.DateTimeFormat(currentIntlLocale(), {
            timeZone: 'UTC', weekday: 'long', day: 'numeric', month: 'long'
        }).format(date);
    }

    function initializeWorkspaceCalendar() {
        if (workspaceSelectedDate) return;
        const queryDate = (() => {
            try {
                const value = new URLSearchParams(window.location.search).get('date');
                return /^\d{4}-\d{2}-\d{2}$/.test(value || '') ? value : '';
            } catch (_) { return ''; }
        })();
        workspaceSelectedDate = queryDate || tashkentDateKey();
        workspaceCalendarMonth = workspaceSelectedDate.slice(0, 7);
    }

    async function loadWorkspaceNotes() {
        if (workspaceNotesLoading) return;
        initializeWorkspaceCalendar();
        workspaceNotesLoading = true;
        const summary = document.getElementById('workspace-planner-summary');
        if (summary) summary.textContent = 'Обновление заметок...';
        try {
            const start = calendarGridStart(workspaceCalendarMonth);
            const end = addCalendarDays(start, 42);
            const feed = await apiFetch('/api/workspace-notes?from=' + encodeURIComponent(start + 'T00:00:00+05:00') +
                '&to=' + encodeURIComponent(end + 'T00:00:00+05:00'));
            workspaceNotes = Array.isArray(feed.items) ? feed.items : [];
            workspaceCanShareNotes = feed.canShare === true;
            workspaceNoteRecipients = Array.isArray(feed.recipients) ? feed.recipients : [];
            syncWorkspaceNoteAudienceOptions();
            renderWorkspaceCalendar();
        } finally {
            workspaceNotesLoading = false;
        }
    }

    function renderWorkspaceCalendar() {
        const grid = document.getElementById('workspace-calendar-grid');
        const monthLabel = document.getElementById('workspace-calendar-month');
        const summary = document.getElementById('workspace-planner-summary');
        if (!grid || !monthLabel) return;
        const [year, month] = workspaceCalendarMonth.split('-').map(Number);
        monthLabel.textContent = workspaceMonthNames.format(new Date(Date.UTC(year, month - 1, 1)));
        const notesByDate = new Map();
        workspaceNotes.forEach(note => {
            const key = tashkentDateKey(note.scheduledFor);
            if (!notesByDate.has(key)) notesByDate.set(key, []);
            notesByDate.get(key).push(note);
        });
        const start = calendarGridStart(workspaceCalendarMonth);
        const today = tashkentDateKey();
        const cells = [];
        for (let index = 0; index < 42; index++) {
            const key = addCalendarDays(start, index);
            const day = Number(key.slice(8));
            const items = notesByDate.get(key) || [];
            const active = items.filter(note => !note.completedAt);
            const classes = [key.slice(0, 7) === workspaceCalendarMonth ? '' : 'outside',
                key === today ? 'today' : '', key === workspaceSelectedDate ? 'selected' : '',
                active.length ? 'has-notes' : '', active.some(note => new Date(note.scheduledFor) < new Date()) ? 'has-overdue' : '']
                .filter(Boolean).join(' ');
            cells.push('<button type="button" class="' + classes + '" onclick="selectWorkspaceCalendarDate(\'' + key + '\')" aria-label="' +
                escapeHtml(selectedDateTitle(key)) + (active.length ? ', заметок: ' + active.length : '') + '"><span>' + day + '</span>' +
                (active.length ? '<b>' + active.length + '</b>' : '') + '</button>');
        }
        grid.innerHTML = cells.join('');
        if (summary) {
            const activeCount = workspaceNotes.filter(note => !note.completedAt).length;
            summary.textContent = activeCount ? 'Активных заметок в календаре: ' + activeCount : 'Нет активных заметок';
        }
        renderWorkspaceAgenda();
    }

    function renderWorkspaceAgenda() {
        const list = document.getElementById('workspace-note-list');
        const title = document.getElementById('workspace-agenda-date');
        const count = document.getElementById('workspace-agenda-count');
        if (!list || !title || !count) return;
        title.textContent = selectedDateTitle(workspaceSelectedDate);
        const items = workspaceNotes
            .filter(note => tashkentDateKey(note.scheduledFor) === workspaceSelectedDate)
            .sort((left, right) => new Date(left.scheduledFor) - new Date(right.scheduledFor));
        count.textContent = items.length ? 'Заметок: ' + items.length : 'Нет заметок';
        if (!items.length) {
            list.innerHTML = '<span class="workspace-note-empty"><i data-lucide="calendar-check-2"></i><strong>На этот день записей нет</strong></span>';
            renderIcons();
            return;
        }
        const canEdit = document.getElementById('workspace-planner')?.dataset.canEdit === 'true';
        list.innerHTML = items.map(note => {
            const completed = Boolean(note.completedAt);
            const author = note.createdByDisplayName || note.createdByUsername || 'Пользователь';
            const completedBy = note.completedByDisplayName || note.completedByUsername;
            const canModify = canEdit && note.canModify === true;
            const audience = note.isVisibleToAll
                ? 'Всем'
                : note.recipientUserId
                    ? (note.recipientDisplayName || note.recipientUsername || 'Выбранному аккаунту')
                    : 'Только автору';
            return '<article class="workspace-note ' + escapeHtml(note.tone || 'info') + (completed ? ' completed' : '') + '">' +
                '<header><time>' + escapeHtml(noteTime(note.scheduledFor)) + '</time><strong>' + escapeHtml(note.title) + '</strong></header>' +
                (note.body ? '<p>' + escapeHtml(note.body) + '</p>' : '') +
                '<footer><span>Автор: <strong>' + escapeHtml(author) + '</strong> · Кому: <strong>' + escapeHtml(audience) + '</strong>' +
                    (completed ? ' · Выполнил: <strong>' + escapeHtml(completedBy || 'Пользователь') + '</strong>' : '') + '</span>' +
                    (canModify ? '<span class="workspace-note-actions"><button class="icon-button" type="button" onclick="toggleWorkspaceNoteComplete(' + note.id + ',' + (!completed) + ')" title="' + (completed ? 'Вернуть в работу' : 'Отметить выполненной') + '" aria-label="' + (completed ? 'Вернуть в работу' : 'Отметить выполненной') + '"><i data-lucide="' + (completed ? 'rotate-ccw' : 'check') + '"></i></button>' +
                        '<button class="icon-button" type="button" onclick="editWorkspaceNote(' + note.id + ')" title="Изменить" aria-label="Изменить заметку"><i data-lucide="pencil"></i></button>' +
                        '<button class="icon-button danger" type="button" onclick="deleteWorkspaceNote(' + note.id + ')" title="Удалить" aria-label="Удалить заметку"><i data-lucide="trash-2"></i></button></span>' : '') + '</footer></article>';
        }).join('');
        renderIcons();
    }

    window.toggleWorkspacePlanner = function (event) {
        if (event) event.stopPropagation();
        const popover = document.getElementById('workspace-planner-popover');
        const button = document.querySelector('.planner-button');
        if (!popover || !button) return;
        const open = popover.hidden;
        if (open) {
            closeAccountNotifications();
            closeSystemMetrics();
        }
        popover.hidden = !open;
        button.setAttribute('aria-expanded', open ? 'true' : 'false');
        if (open) loadWorkspaceNotes().catch(error => showToast(error.message, 'error'));
    };

    window.closeWorkspacePlanner = function () {
        const popover = document.getElementById('workspace-planner-popover');
        const button = document.querySelector('.planner-button');
        if (popover) popover.hidden = true;
        if (button) button.setAttribute('aria-expanded', 'false');
        closeWorkspaceNoteEditor();
    };

    window.changeWorkspaceCalendarMonth = function (amount) {
        initializeWorkspaceCalendar();
        const [year, month] = workspaceCalendarMonth.split('-').map(Number);
        const next = new Date(Date.UTC(year, month - 1 + Number(amount), 1));
        workspaceCalendarMonth = calendarKey(next.getUTCFullYear(), next.getUTCMonth() + 1, 1).slice(0, 7);
        workspaceSelectedDate = workspaceCalendarMonth + '-01';
        loadWorkspaceNotes().catch(error => showToast(error.message, 'error'));
    };

    window.selectWorkspaceCalendarToday = function () {
        workspaceSelectedDate = tashkentDateKey();
        workspaceCalendarMonth = workspaceSelectedDate.slice(0, 7);
        loadWorkspaceNotes().catch(error => showToast(error.message, 'error'));
    };

    window.selectWorkspaceCalendarDate = function (key) {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(key)) return;
        workspaceSelectedDate = key;
        if (key.slice(0, 7) !== workspaceCalendarMonth) {
            workspaceCalendarMonth = key.slice(0, 7);
            loadWorkspaceNotes().catch(error => showToast(error.message, 'error'));
            return;
        }
        renderWorkspaceCalendar();
        closeWorkspaceNoteEditor();
    };

    window.openWorkspaceNoteEditor = function (note) {
        initializeWorkspaceCalendar();
        const form = document.getElementById('workspace-note-form');
        if (!form) return;
        document.getElementById('workspace-note-id').value = note?.id || '';
        document.getElementById('workspace-note-title').value = note?.title || '';
        document.getElementById('workspace-note-body').value = note?.body || '';
        document.getElementById('workspace-note-tone').value = note?.tone || 'info';
        document.getElementById('workspace-note-time').value = note ? noteTime(note.scheduledFor) : '09:00';
        const audienceValue = note?.isVisibleToAll
            ? 'all'
            : note?.recipientUserId
                ? 'user:' + note.recipientUserId
                : 'private';
        syncWorkspaceNoteAudienceOptions(audienceValue, note);
        form.hidden = false;
        document.getElementById('workspace-note-title').focus();
        renderIcons();
    };

    window.closeWorkspaceNoteEditor = function () {
        const form = document.getElementById('workspace-note-form');
        if (form) form.hidden = true;
    };

    window.editWorkspaceNote = function (id) {
        const note = workspaceNotes.find(item => Number(item.id) === Number(id));
        if (!note) return;
        workspaceSelectedDate = tashkentDateKey(note.scheduledFor);
        renderWorkspaceCalendar();
        openWorkspaceNoteEditor(note);
    };

    window.saveWorkspaceNote = async function (event) {
        event.preventDefault();
        const form = event.currentTarget;
        const id = Number(document.getElementById('workspace-note-id').value || 0);
        const time = document.getElementById('workspace-note-time').value;
        const scheduled = new Date(workspaceSelectedDate + 'T' + time + ':00+05:00');
        const audience = document.getElementById('workspace-note-audience')?.value || 'private';
        const payload = {
            title: document.getElementById('workspace-note-title').value,
            body: document.getElementById('workspace-note-body').value,
            tone: document.getElementById('workspace-note-tone').value,
            scheduledFor: scheduled.toISOString(),
            isVisibleToAll: audience === 'all',
            recipientUserId: audience.startsWith('user:') ? Number(audience.slice(5)) : null
        };
        const submit = form.querySelector('[type="submit"]');
        submit.disabled = true;
        try {
            await apiFetch(id ? '/api/workspace-notes/' + id : '/api/workspace-notes', {
                method: id ? 'PUT' : 'POST', body: payload
            });
            closeWorkspaceNoteEditor();
            await loadWorkspaceNotes();
            await loadAccountNotifications(false);
            showToast(id ? 'Заметка обновлена' : 'Заметка добавлена', 'success');
        } catch (error) { showToast(error.message, 'error'); }
        finally { submit.disabled = false; }
    };

    window.toggleWorkspaceNoteComplete = async function (id, completed) {
        try {
            await apiPost('/api/workspace-notes/' + id + '/complete', { completed });
            await loadWorkspaceNotes();
            await loadAccountNotifications(false);
        } catch (error) { showToast(error.message, 'error'); }
    };

    window.deleteWorkspaceNote = function (id) {
        confirmDialog('Удалить заметку', 'Заметка будет удалена из календаря.', async function () {
            try {
                await apiFetch('/api/workspace-notes/' + id, { method: 'DELETE' });
                await loadWorkspaceNotes();
                await loadAccountNotifications(false);
                showToast('Заметка удалена', 'success');
            } catch (error) { showToast(error.message, 'error'); }
        });
    };

    let systemMetricsLoading = false;

    function closeSystemMetrics() {
        const popover = document.getElementById('system-metrics-popover');
        const button = document.querySelector('.system-load-button');
        if (popover) popover.hidden = true;
        if (button) button.setAttribute('aria-expanded', 'false');
    }

    function closeAccountNotifications() {
        const popover = document.getElementById('notification-popover');
        const button = document.querySelector('.notification-button');
        if (popover) popover.hidden = true;
        if (button) button.setAttribute('aria-expanded', 'false');
    }

    window.toggleSystemMetrics = function (event) {
        if (event) event.stopPropagation();
        const popover = document.getElementById('system-metrics-popover');
        const button = document.querySelector('.system-load-button');
        if (!popover || !button) return;
        const shouldOpen = popover.hidden;
        if (shouldOpen) {
            closeAccountNotifications();
            closeWorkspacePlanner();
        }
        popover.hidden = !shouldOpen;
        button.setAttribute('aria-expanded', shouldOpen ? 'true' : 'false');
        if (shouldOpen) loadSystemMetrics();
    };

    function setSystemMetricText(id, value) {
        const element = document.getElementById(id);
        if (element) element.textContent = value;
    }

    function metricPercent(value) {
        const number = Number(value);
        return Number.isFinite(number) ? Math.max(0, Math.min(100, number)) : 0;
    }

    function formatMemory(megabytes) {
        const value = Number(megabytes);
        if (!Number.isFinite(value) || value < 0) return '—';
        if (value >= 1024) return (value / 1024).toLocaleString(currentIntlLocale(), { maximumFractionDigits: 1 }) + ' ГБ';
        return Math.round(value).toLocaleString(currentIntlLocale()) + ' МБ';
    }

    function formatSystemUptime(seconds) {
        const totalMinutes = Math.max(0, Math.floor(Number(seconds || 0) / 60));
        if (totalMinutes < 1) return 'меньше минуты';
        const days = Math.floor(totalMinutes / 1440);
        const hours = Math.floor(totalMinutes % 1440 / 60);
        const minutes = totalMinutes % 60;
        if (days > 0) return days + ' д ' + hours + ' ч';
        if (hours > 0) return hours + ' ч ' + minutes + ' мин';
        return minutes + ' мин';
    }

    function formatProcessorCores(physical, logical) {
        const count = Math.max(0, Number(physical || 0));
        const lastTwo = count % 100;
        const last = count % 10;
        const word = last === 1 && lastTwo !== 11
            ? 'физ. ядро'
            : last >= 2 && last <= 4 && (lastTwo < 12 || lastTwo > 14)
                ? 'физ. ядра'
                : 'физ. ядер';
        return count + ' ' + word + ' · ' + Math.max(0, Number(logical || 0)) + ' логич.';
    }

    function applyMetricTone(element, percent) {
        if (!element) return;
        element.classList.toggle('is-warning', percent >= 75 && percent < 90);
        element.classList.toggle('is-critical', percent >= 90);
    }

    function renderSystemMetrics(metrics) {
        const cpu = metricPercent(metrics.cpuPercent);
        const ram = metricPercent(metrics.memoryPercent);
        const processCpu = metricPercent(metrics.processCpuPercent);
        const button = document.querySelector('.system-load-button');
        const error = document.getElementById('system-metrics-error');
        const cpuMeter = document.getElementById('system-cpu-meter');
        const ramMeter = document.getElementById('system-ram-meter');
        const pressure = Math.max(cpu, ram);

        if (button) {
            button.classList.remove('is-unavailable');
            applyMetricTone(button, pressure);
            button.title = 'CPU ' + Math.round(cpu) + '% · RAM ' + Math.round(ram) + '%';
        }
        if (error) error.hidden = true;
        setSystemMetricText('system-cpu-value', Math.round(cpu) + '%');
        setSystemMetricText('system-ram-value', Math.round(ram) + '%');
        setSystemMetricText('system-detail-cpu', cpu.toLocaleString(currentIntlLocale(), { maximumFractionDigits: 1 }) + '%');
        setSystemMetricText('system-detail-ram', ram.toLocaleString(currentIntlLocale(), { maximumFractionDigits: 1 }) + '%');
        setSystemMetricText('system-ram-caption', formatMemory(metrics.memoryUsedMb) + ' из ' + formatMemory(metrics.memoryTotalMb));
        setSystemMetricText('system-process-load', processCpu.toLocaleString(currentIntlLocale(), { maximumFractionDigits: 1 }) + '% CPU');
        setSystemMetricText('system-process-memory', formatMemory(metrics.processWorkingSetMb) + ' · GC ' + formatMemory(metrics.gcHeapMb));
        setSystemMetricText('system-active-checks', Number(metrics.activeChecks || 0) + ' из ' + Number(metrics.maxConcurrentChecks || 0));
        setSystemMetricText('system-core-count', formatProcessorCores(metrics.physicalCoreCount, metrics.logicalProcessorCount));
        setSystemMetricText('system-thread-count', String(Number(metrics.processThreadCount || 0)));
        setSystemMetricText('system-uptime', formatSystemUptime(metrics.uptimeSeconds));
        const capturedAt = new Date(metrics.capturedAt);
        setSystemMetricText('system-metrics-updated', Number.isNaN(capturedAt.getTime())
            ? 'Обновлено сейчас'
            : 'Обновлено ' + capturedAt.toLocaleTimeString(currentIntlLocale(), { timeZone: 'Asia/Tashkent', hour: '2-digit', minute: '2-digit', second: '2-digit' }));

        [cpuMeter, ramMeter].forEach((meter, index) => {
            const value = index === 0 ? cpu : ram;
            if (!meter) return;
            meter.style.width = value + '%';
            applyMetricTone(meter, value);
        });
    }

    async function loadSystemMetrics() {
        if (systemMetricsLoading || !document.getElementById('system-load')) return;
        systemMetricsLoading = true;
        try {
            renderSystemMetrics(await apiFetch('/api/system/metrics'));
        } catch (_) {
            const button = document.querySelector('.system-load-button');
            const error = document.getElementById('system-metrics-error');
            if (button) {
                button.classList.remove('is-warning', 'is-critical');
                button.classList.add('is-unavailable');
                button.title = 'Данные о нагрузке временно недоступны';
            }
            if (error) error.hidden = false;
            setSystemMetricText('system-cpu-value', '—');
            setSystemMetricText('system-ram-value', '—');
            setSystemMetricText('system-metrics-updated', 'Нет связи с сервером');
        } finally {
            systemMetricsLoading = false;
        }
    }

    function startSystemMetrics() {
        if (!document.getElementById('system-load')) return;
        loadSystemMetrics();
        window.setInterval(() => {
            if (document.visibilityState === 'visible') loadSystemMetrics();
        }, 5000);
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') loadSystemMetrics();
        });
    }

    function updateNavigationCounters(detail) {
        const requests = [];
        const cameraCount = document.getElementById('nav-camera-count');
        if (cameraCount && detail.topics.includes('cameras')) {
            requests.push(apiFetch('/api/health/status')
                .then(summary => {
                    if (summary) cameraCount.textContent = summary.total;
                }).catch(() => { }));
        }

        const alertCount = document.getElementById('nav-alert-count');
        const historyCount = document.getElementById('nav-history-alert-count');
        if (alertCount || historyCount) {
            requests.push(apiFetch('/api/alerts/summary')
                .then(summary => {
                    if (!summary) return;
                    const counters = {
                        'nav-alert-count': summary.active,
                        'nav-all-alert-count': summary.active,
                        'nav-physical-alert-count': summary.physical,
                        'nav-technical-alert-count': summary.technical,
                        'nav-history-alert-count': summary.history
                    };
                    Object.entries(counters).forEach(([id, value]) => {
                        const element = document.getElementById(id);
                        if (element) element.textContent = value;
                    });
                }).catch(() => { }));
        }

        return Promise.all(requests);
    }

    // Server-side checks update the database independently. Pages subscribe to this
    // event and refresh only their visible data without losing the current UI state.
    function startDataAutoRefresh() {
        if (window.location.pathname.toLowerCase().startsWith('/login')) return;

        const refreshIntervalMs = 60000;
        const allTopics = ['cameras', 'registry', 'alerts', 'notifications', 'navigation', 'access', 'users', 'settings', 'queue', 'maintenance'];
        let pendingTopics = new Set();
        let pendingCameraIds = new Set();
        let allCameras = false;
        let timer = null;
        let running = false;
        let pendingReason = 'initial';
        const canRefresh = () => {
            if (document.visibilityState !== 'visible') return false;
            if (document.body.classList.contains('alert-dragging')) return false;
            if (document.body.classList.contains('alert-moving')) return false;
            return true;
        };

        window.canRefreshVisibleData = canRefresh;

        const canFlush = () => canRefresh() ||
            (document.visibilityState === 'visible' && (pendingTopics.has('notifications') || pendingTopics.has('access')));
        const schedule = () => {
            if (timer !== null || running || !pendingTopics.size || !canFlush()) return;
            timer = window.setTimeout(flush, 500);
        };
        const flush = async () => {
            timer = null;
            if (!canFlush()) return;
            running = true;
            const tasks = [];
            const topics = canRefresh() ? Array.from(pendingTopics) : Array.from(pendingTopics).filter(topic => topic === 'notifications' || topic === 'access');
            const detail = {
                topics,
                cameraIds: allCameras ? null : Array.from(pendingCameraIds),
                reason: pendingReason,
                requestedAt: Date.now(),
                waitUntil: task => tasks.push(Promise.resolve(task))
            };
            topics.forEach(topic => pendingTopics.delete(topic));
            if (topics.includes('cameras')) {
                pendingCameraIds = new Set();
                allCameras = false;
            }
            try {
                // Permission changes invalidate the entire authorized page, including open dialogs.
                if (detail.topics.includes('access') && window.refreshWorkspaceAccess && !await window.refreshWorkspaceAccess()) return;
                if (detail.topics.includes('navigation')) tasks.push(updateNavigationCounters(detail));
                if (detail.topics.includes('notifications')) tasks.push(loadAccountNotifications(true));
                document.dispatchEvent(new CustomEvent('camera-maintainer:data-refresh', { detail }));
                await Promise.allSettled(tasks);
            } finally {
                running = false;
                schedule();
            }
        };
        const refreshVisibleData = (reason, topics = allTopics, cameraIds = null) => {
            topics.forEach(topic => pendingTopics.add(topic));
            if (topics.includes('cameras')) {
                if (!cameraIds) allCameras = true;
                else cameraIds.forEach(id => pendingCameraIds.add(Number(id)));
            }
            pendingReason = reason || 'manual';
            schedule();
        };
        window.refreshVisibleData = refreshVisibleData;

        // Remove markers left by versions that used full-page refreshes.
        const currentUrl = new URL(window.location.href);
        if (currentUrl.searchParams.has('__cm_refresh')) {
            currentUrl.searchParams.delete('__cm_refresh');
            window.history.replaceState(null, document.title, currentUrl.toString());
        }

        window.setInterval(() => {
            refreshVisibleData('fallback');
        }, refreshIntervalMs);

        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') refreshVisibleData('visibility');
        });
        document.addEventListener('focusout', () => window.setTimeout(schedule, 0));
        new MutationObserver(schedule).observe(document.body, { attributes: true, attributeFilter: ['class'] });
        refreshVisibleData('initial');
    }

    document.addEventListener('DOMContentLoaded', function () {
        const mobileSidebarQuery = window.matchMedia('(max-width: 700px)');
        if (!mobileSidebarQuery.matches && localStorage.getItem('cm_sidebar_collapsed') === '1')
            document.body.classList.add('sidebar-collapsed');
        const sidebarToggle = document.getElementById('sidebar-toggle');
        const syncSidebarForViewport = () => {
            const sidebar = document.getElementById('sidebar');
            const backdrop = document.getElementById('sidebar-backdrop');
            if (mobileSidebarQuery.matches) {
                sidebar?.classList.remove('mobile-open');
                backdrop?.classList.remove('visible');
                sidebarToggle?.setAttribute('aria-expanded', 'false');
                return;
            }

            sidebar?.classList.remove('mobile-open');
            backdrop?.classList.remove('visible');
            const sidebarExpanded = !document.body.classList.contains('sidebar-collapsed');
            sidebarToggle?.setAttribute('aria-expanded', sidebarExpanded ? 'true' : 'false');
        };
        syncSidebarForViewport();
        mobileSidebarQuery.addEventListener('change', syncSidebarForViewport);
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape') {
                closeModal();
                closeImageViewer();
                closeSystemMetrics();
                closeAccountNotifications();
                closeWorkspacePlanner();
                if (mobileSidebarQuery.matches) toggleSidebar(false);
            }
        });
        document.addEventListener('click', event => {
            const notifications = document.getElementById('account-notifications');
            const notificationPopover = document.getElementById('notification-popover');
            if (notifications && notificationPopover && !notificationPopover.hidden && !notifications.contains(event.target))
                closeAccountNotifications();
            const systemLoad = document.getElementById('system-load');
            const systemPopover = document.getElementById('system-metrics-popover');
            if (systemLoad && systemPopover && !systemPopover.hidden && !systemLoad.contains(event.target))
                closeSystemMetrics();
            const planner = document.getElementById('workspace-planner');
            const plannerPopover = document.getElementById('workspace-planner-popover');
            if (planner && plannerPopover && !plannerPopover.hidden && !planner.contains(event.target))
                closeWorkspacePlanner();
        });
        document.querySelectorAll('.zoomable, [data-zoom="true"]').forEach(image => {
            image.addEventListener('dblclick', event => { event.preventDefault(); event.stopPropagation(); openImageViewer(image); });
        });
        renderIcons();
        startTrustedClock();
        startSystemMetrics();
        document.addEventListener('camera-maintainer:data-refresh', event => {
            event.detail.waitUntil(refreshCameraSummary(event.detail));
        });
        startDataAutoRefresh();
        startSignalR();
        try {
            if (document.getElementById('workspace-planner') && new URLSearchParams(window.location.search).get('planner') === '1')
                toggleWorkspacePlanner();
        } catch (_) { }
    });
})();
