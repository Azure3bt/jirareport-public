(() => {
    'use strict';

    const REPORT_ENDPOINTS = {
        cycleTime: '/api/reports/cycle-time',
        timeInStatus: '/api/reports/time-in-status',
        wipAging: '/api/reports/wip-aging',
        sprintScope: '/api/reports/sprint-scope',
        workload: '/api/reports/workload',
        bugRatio: '/api/reports/bug-ratio',
        quarterlyDistribution: '/api/reports/quarterly-distribution',
        sprintVelocity: '/api/reports/sprint-velocity',
        reworkRate: '/api/reports/rework-rate'
    };

    const SPRINTS_ENDPOINT = '/api/reports/sprints';
    const WORKLOG_USERS_ENDPOINT = '/api/reports/worklog-users';

    const $ = (selector) => document.querySelector(selector);
    const charts = new Map();
    const colors = ['#7894ff', '#38c9a7', '#ffb454', '#fb7185', '#b18cff', '#48b9dc'];

    if (window.Chart) {
        Chart.defaults.font.family = "'Vazirmatn', sans-serif";
        Chart.defaults.color = '#aebbd0';
        Chart.defaults.plugins.legend.labels.usePointStyle = true;
    }

    const projectKey = () => {
        const input = document.getElementById('projectKeyInput');
        return input ? input.value.trim() : '';
    };

    const currentSprint = () => {
        const select = $('#sprintFilter');
        return (select && select.value ? select.value.trim() : '');
    };

    const makeUrl = (path, overrideSprint = null) => {
        const url = new URL(path, window.location.origin);
        const pk = projectKey();
        const sprint = overrideSprint !== null ? overrideSprint : currentSprint();
        if (pk) {
            url.searchParams.set('projectKey', pk);
        }
        if (sprint) {
            url.searchParams.set('sprintName', sprint);
        }
        return url.toString();
    };

    async function fetchJson(url, options) {
        const response = await fetch(url, options);
        if (!response.ok) {
            const body = await response.text();
            throw new Error(body || `خطای سرور (${response.status})`);
        }
        if (response.status === 204) return null;
        return response.json();
    }

    const rowsOf = (payload) => {
        if (!payload) return [];
        if (Array.isArray(payload)) return payload;
        const value = payload?.items ?? payload?.data ?? payload?.results ?? payload?.values ?? payload?.issues;
        if (Array.isArray(value)) return value;

        if (typeof payload === 'object') {
            return Object.entries(payload).map(([k, v]) => ({ name: k, value: v }));
        }
        return [];
    };

    const first = (obj, keys) => {
        if (!obj || typeof obj !== 'object') return undefined;
        const objKeys = Object.keys(obj);
        for (const key of keys) {
            const lowerKey = key.toLowerCase();
            const matchedKey = objKeys.find(k => k.toLowerCase() === lowerKey);
            if (matchedKey && obj[matchedKey] !== undefined && obj[matchedKey] !== null) {
                return obj[matchedKey];
            }
        }
        return undefined;
    };

    const toNumber = (val, fallback = 0) => {
        if (val === undefined || val === null || val === '') return fallback;
        const num = Number(val);
        return Number.isFinite(num) ? num : fallback;
    };

    const labelsOf = (payload, rows) =>
        rows.map((r, i) => String(first(r, ['label', 'name', 'status', 'statusName', 'assignee', 'sprint', 'key', 'issueKey']) ?? `آیتم ${i + 1}`));

    const numsOf = (payload, rows, keys = ['value', 'count', 'days', 'cycleTimeDays', 'durationDays', 'averageDays', 'total', 'hours', 'issues']) =>
        rows.map(r => toNumber(first(r, keys)));

    function showChartError(id, message = '') {
        const el = document.querySelector(`[data-chart-error="${id}"]`);
        if (el) el.textContent = message;
    }

    function draw(id, payload, type, title, opts = {}) {
        const canvas = document.getElementById(id);
        if (!canvas || !window.Chart) return;

        const rows = rowsOf(payload);
        let labels = [];
        let values = [];
        let datasets;
        let tooltipLabel;

        if (id === 'quarterlyChart') {
            labels = rows.map((r, i) => String(first(r, ['quarter', 'period', 'label', 'name', 'sprint']) ?? `دوره ${i + 1}`));
            if (!rows.length && Array.isArray(payload?.labels)) labels = payload.labels.map(String);
            const categories = [
                { label: 'Features', keys: ['features', 'featureCount', 'feature', 'featuresCount'], color: colors[0] },
                { label: 'Bugs', keys: ['bugs', 'bugCount', 'bug', 'bugsCount'], color: colors[3] },
                { label: 'Unplanned', keys: ['unplanned', 'unplannedCount', 'unplannedWork'], color: colors[2] }
            ];
            datasets = categories.map(category => ({
                label: category.label,
                data: rows.length
                    ? rows.map(r => toNumber(first(r, category.keys)))
                    : (Array.isArray(payload?.[category.label.toLowerCase()]) ? payload[category.label.toLowerCase()].map(v => toNumber(v)) : []),
                backgroundColor: category.color,
                borderColor: category.color,
                borderWidth: 1,
                borderRadius: 4
            }));
        } else if (id === 'velocityChart') {
            if (!rows.length) {
                showChartError(id, 'داده‌ای برای رسم نمودار وجود ندارد.');
                return;
            }

            labels = rows.map(r => String(first(r, ['sprintName', 'sprint', 'name']) ?? 'اسپرینت'));
            const committedData = rows.map(r => toNumber(first(r, ['committed', 'committedPoints'])));
            const completedData = rows.map(r => toNumber(first(r, ['completed', 'completedPoints', 'done'])));
            const sayDoData = rows.map(r => toNumber(first(r, ['sayDoRatio', 'sayDo', 'ratio'])));

            datasets = [
                {
                    label: 'تعهد شده (Committed)',
                    data: committedData,
                    backgroundColor: 'rgba(54, 162, 235, 0.6)',
                    borderColor: 'rgba(54, 162, 235, 1)',
                    borderWidth: 1
                },
                {
                    label: 'تکمیل شده (Completed)',
                    data: completedData,
                    backgroundColor: 'rgba(75, 192, 192, 0.6)',
                    borderColor: 'rgba(75, 192, 192, 1)',
                    borderWidth: 1
                },
                {
                    type: 'line',
                    label: 'نسبت انجام به تعهد (Say/Do %)',
                    data: sayDoData,
                    yAxisID: 'y1',
                    borderColor: colors[2],
                    backgroundColor: colors[2],
                    borderWidth: 2,
                    pointRadius: 3,
                    tension: 0.25
                }
            ];
            showChartError(id, '');
        } else if (id === 'sprintScopeChart') {
            labels = rows.map((r, i) => String(first(r, ['sprint', 'sprintName', 'name', 'label']) ?? `اسپرینت ${i + 1}`));
            if (!rows.length && Array.isArray(payload?.labels)) labels = payload.labels.map(String);

            const completed = rows.map(r => toNumber(first(r, ['completedScope', 'completed', 'done'])));
            const remaining = rows.map(r => toNumber(first(r, ['remainingScope', 'remaining', 'inProgress', 'initialCommitment'])));

            datasets = [
                {
                    label: 'تکمیل شده (Completed)',
                    data: completed,
                    backgroundColor: colors[1],
                    borderColor: colors[1],
                    borderWidth: 1,
                    borderRadius: 4
                },
                {
                    label: 'باقی‌مانده (Remaining)',
                    data: remaining,
                    backgroundColor: colors[2],
                    borderColor: colors[2],
                    borderWidth: 1,
                    borderRadius: 4
                }
            ];
            tooltipLabel = context => {
                const r = rows[context.dataIndex] || {};
                const total = first(r, ['totalItems', 'initialCommitment']) ?? '';
                return `${context.dataset.label}: ${context.formattedValue}${total ? ` (کل: ${total})` : ''}`;
            };
        } else if (id === 'reworkChart') {
            labels = rows.map((r, i) => String(first(r, ['sprint', 'sprintName', 'period', 'label', 'name']) ?? `دوره ${i + 1}`));
            if (!rows.length && Array.isArray(payload?.labels)) labels = payload.labels.map(String);
            values = rows.length
                ? rows.map(r => toNumber(first(r, ['reworkRate', 'reworkRatePercentage', 'rate', 'percentage', 'percent', 'value'])))
                : (payload?.values ?? payload?.reworkRates ?? []).map(v => toNumber(v));
        } else if (id === 'cycleTimeChart') {
            const items = rows.length ? rows : (payload?.issues ?? []);
            if (items.length) {
                labels = items.map((r, i) => {
                    const rawDate = first(r, ['resolved', 'resolvedDate', 'created']);
                    if (rawDate) {
                        const d = new Date(rawDate);
                        return !isNaN(d.getTime()) ? d.toLocaleDateString('fa-IR') : (first(r, ['key', 'issueKey']) || `آیتم ${i + 1}`);
                    }
                    return first(r, ['key', 'issueKey']) || `آیتم ${i + 1}`;
                });
                values = items.map(r => {
                    const days = first(r, ['cycleTimeDays', 'days', 'value']);
                    if (days !== undefined && days !== null) return Number(toNumber(days).toFixed(1));
                    const hours = first(r, ['cycleTimeHours', 'leadTimeHours', 'hours']);
                    if (hours !== undefined && hours !== null) return Number((toNumber(hours) / 24).toFixed(1));
                    return 0;
                });
            }
        } else if (id === 'wipAgingChart') {
            const visibleRows = rows.slice(0, 12);
            labels = visibleRows.map((r, i) => String(first(r, ['key', 'issueKey', 'summary', 'title']) ?? `تسک ${i + 1}`));
            values = visibleRows.map(r => Number(toNumber(first(r, ['ageDays', 'days', 'age', 'value'])).toFixed(1)));
            tooltipLabel = context => {
                const issue = visibleRows[context.dataIndex] || {};
                const age = toNumber(first(issue, ['ageDays', 'days', 'age', 'value'])).toFixed(1);
                return [
                    `${first(issue, ['key', 'issueKey']) ?? ''}: ${first(issue, ['summary', 'title']) ?? ''}`,
                    `وضعیت: ${first(issue, ['status', 'statusName']) ?? 'نامشخص'}`,
                    `سن: ${age} روز`
                ];
            };
        } else if (id === 'workloadChart') {
            labels = rows.map(r => first(r, ['assignee', 'name']) || 'نامشخص');
            values = rows.map(r => toNumber(first(r, ['openCount', 'storyPointsOpen', 'count', 'value'])));
        } else {
            labels = labelsOf(payload, rows);
            values = numsOf(payload, rows, opts.valueKeys);
            if (!rows.length && Array.isArray(payload?.labels) && Array.isArray(payload?.values)) {
                labels = payload.labels.map(String);
                values = payload.values.map(v => toNumber(v));
            }
            if (!labels.length) {
                const scalar = first(payload, opts.valueKeys ?? ['value', 'count', 'total']);
                if (scalar !== undefined) { labels = ['مجموع']; values = [toNumber(scalar)]; }
            }
        }

        if ((id === 'quarterlyChart' || id === 'sprintScopeChart' || id === 'velocityChart') && datasets) {
            if (!labels.length || datasets.every(ds => !ds.data.length)) throw new Error('داده‌ای برای رسم نمودار وجود ندارد.');
        } else if (!labels.length || !values.length) {
            throw new Error('داده‌ای برای رسم نمودار وجود ندارد.');
        }

        const isPie = type === 'doughnut';
        if (!datasets) {
            datasets = [{
                label: title,
                data: values,
                backgroundColor: isPie ? colors : colors[0],
                borderColor: isPie ? '#111c2e' : colors[0],
                borderWidth: isPie ? 2 : 1,
                borderRadius: isPie ? 0 : 5,
                tension: 0.3,
                fill: false,
                ...(id === 'wipAgingChart' ? { maxBarThickness: 18 } : {})
            }];
        }
        if (charts.has(id)) charts.get(id).destroy();

        const chart = new Chart(canvas, {
            type,
            data: { labels, datasets },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                indexAxis: opts.horizontal ? 'y' : 'x',
                scales: isPie ? {} : {
                    x: {
                        stacked: !!opts.stacked,
                        beginAtZero: !!opts.horizontal,
                        grid: { color: '#24334a' },
                        ...(id === 'wipAgingChart' ? { title: { display: true, text: 'سن (روز)' } } : {})
                    },
                    y: {
                        stacked: !!opts.stacked,
                        beginAtZero: !opts.horizontal,
                        grid: { color: '#24334a' },
                        ...(id === 'wipAgingChart' ? { ticks: { autoSkip: false, font: { size: 11 } } } : {})
                    },
                    ...(id === 'velocityChart' ? {
                        y1: {
                            beginAtZero: true,
                            suggestedMax: 100,
                            position: 'right',
                            grid: { drawOnChartArea: false },
                            ticks: { callback: value => `${value}%` }
                        }
                    } : {})
                },
                plugins: {
                    legend: { display: isPie || id === 'quarterlyChart' || id === 'velocityChart' || id === 'sprintScopeChart' },
                    tooltip: { rtl: true, ...(tooltipLabel ? { callbacks: { label: tooltipLabel } } : {}) }
                }
            }
        });
        charts.set(id, chart);
    }

    function updateQuarterlyInsight(payload) {
        const el = document.getElementById('quarterlyInsight');
        if (!el) return;
        const explicit = first(payload, ['insight', 'insights', 'summary', 'message']);
        if (explicit !== undefined) {
            el.textContent = Array.isArray(explicit) ? explicit.join(' ') : String(explicit);
            return;
        }
        const rows = rowsOf(payload);
        if (!rows.length) { el.textContent = ''; return; }
        const totals = rows.map((r, i) => ({
            label: String(first(r, ['quarter', 'period', 'label', 'name', 'sprint']) ?? `دوره ${i + 1}`),
            bugs: toNumber(first(r, ['bugs', 'bugCount', 'bug', 'bugsCount'])),
            unplanned: toNumber(first(r, ['unplanned', 'unplannedCount', 'unplannedWork']))
        }));
        const peak = totals.reduce((a, b) => (b.bugs + b.unplanned > a.bugs + a.unplanned ? b : a));
        el.textContent = `بیشترین مجموع کارهای باگ و برنامه‌ریزی‌نشده مربوط به ${peak.label} است (${peak.bugs + peak.unplanned}).`;
    }

    const setKpi = (id, value, suffix = '') => {
        const el = document.getElementById(id);
        if (el) el.textContent = (value === undefined || value === null || value === '') ? '—' : `${value}${suffix}`;
    };

    function updateKpis(data) {
        const cycle = data.cycleTime, wip = data.wipAging, bugs = data.bugRatio;

        let total = first(cycle, ['totalIssues', 'totalCount', 'issueCount', 'count', 'total']);
        if (total === undefined) total = rowsOf(cycle).length || first(wip, ['totalIssues', 'totalCount', 'issueCount']);
        setKpi('kpi-total', total);

        let avg = first(cycle, ['averageCycleTimeDays', 'avgCycleTimeDays', 'averageDays', 'average', 'avg']);
        if (avg === undefined) {
            const vals = numsOf(cycle, rowsOf(cycle), ['cycleTimeDays', 'days', 'durationDays', 'value']);
            if (vals.length) avg = vals.reduce((a, b) => a + b, 0) / vals.length;
        }
        setKpi('kpi-cycle', avg === undefined ? undefined : Number(avg).toFixed(1), avg === undefined ? '' : ' روز');

        let wipCount = first(wip, ['wipCount', 'totalWip', 'totalCount', 'count', 'total']);
        if (wipCount === undefined && rowsOf(wip).length) wipCount = rowsOf(wip).length;
        setKpi('kpi-wip', wipCount);

        let ratio = first(bugs, ['bugRatio', 'ratio', 'bugPercentage', 'percentage', 'percent']);
        if (ratio === undefined) {
            const bug = first(bugs, ['bugs', 'bugCount']), all = first(bugs, ['totalIssues', 'totalCount', 'issueCount', 'total']);
            if (bug !== undefined && Number(all)) ratio = (Number(bug) / Number(all)) * 100;
        }
        setKpi('kpi-bug', ratio === undefined ? undefined : `${Number(ratio).toFixed(1)}%`);
    }

    function renderIndividualPerformance(result) {
        const body = $('#individualPerformanceRows');
        const error = $('#individualPerformanceError');
        if (!body) return;
        body.replaceChildren();

        if (result.status === 'rejected') {
            if (error) error.textContent = `دریافت گزارش فردی ناموفق بود: ${result.reason?.message ?? 'خطای ناشناخته'}`;
            const row = document.createElement('tr');
            const cell = document.createElement('td');
            cell.colSpan = 7;
            cell.className = 'text-secondary';
            cell.textContent = 'اطلاعات گزارش در دسترس نیست.';
            row.appendChild(cell);
            body.appendChild(row);
            return false;
        }

        if (error) error.textContent = '';
        const people = Array.isArray(result.value) ? result.value : [];
        if (!people.length) {
            const row = document.createElement('tr');
            const cell = document.createElement('td');
            cell.colSpan = 7;
            cell.className = 'text-secondary';
            cell.textContent = 'برای این پروژه هنوز داده‌ای در دیتابیس نیست.';
            row.appendChild(cell);
            body.appendChild(row);
            return true;
        }

        for (const person of people) {
            const row = document.createElement('tr');
            const values = [
                first(person, ['assignee']) ?? 'نامشخص',
                first(person, ['assignedCount']) ?? 0,
                first(person, ['openCount']) ?? 0,
                first(person, ['resolvedLast30Days']) ?? 0,
                toNumber(first(person, ['storyPointsCompletedLast30Days'])).toFixed(1),
                toNumber(first(person, ['averageCycleTimeDaysLast30Days'])).toFixed(1),
                first(person, ['reworkedIssues']) ?? 0
            ];
            for (const value of values) {
                const cell = document.createElement('td');
                cell.textContent = String(value);
                row.appendChild(cell);
            }
            body.appendChild(row);
        }
        return true;
    }

    async function loadSprintsList() {
        const select = $('#sprintFilter');
        if (!select) return;

        const previousVal = select.value;
        try {
            const url = makeUrl(SPRINTS_ENDPOINT, '');
            const sprints = await fetchJson(url);

            select.innerHTML = '<option value="">همه اسپرینت‌ها (All Sprints)</option>';

            if (Array.isArray(sprints)) {
                sprints.forEach(sprint => {
                    const opt = document.createElement('option');
                    opt.value = sprint;
                    opt.textContent = sprint;
                    if (sprint === previousVal) {
                        opt.selected = true;
                    }
                    select.appendChild(opt);
                });
            }
        } catch (err) {
            console.warn('عدم موفقیت در دریافت لیست اسپرینت‌ها:', err.message);
        }
    }

    async function loadDashboard() {
        const overlay = $('#loadingOverlay'), alert = $('#dashboardError');
        if (overlay) overlay.hidden = false;
        if (alert) { alert.hidden = true; alert.textContent = ''; }

        const names = Object.keys(REPORT_ENDPOINTS);
        const results = await Promise.allSettled([
            ...names.map(name => fetchJson(makeUrl(REPORT_ENDPOINTS[name]))),
            fetchJson(makeUrl('/api/reports/individual-performance'))
        ]);

        const data = {};
        let failures = 0;

        const chartInfo = {
            cycleTime: ['cycleTimeChart', 'line', 'روز'],
            timeInStatus: ['bottleneckChart', 'bar', 'ساعت', { horizontal: true }],
            wipAging: ['wipAgingChart', 'bar', 'روز', { horizontal: true }],
            sprintScope: ['sprintScopeChart', 'bar', 'آیتم', { stacked: true }],
            workload: ['workloadChart', 'doughnut', 'تعداد'],
            bugRatio: ['bugRatioChart', 'doughnut', 'درصد'],
            quarterlyDistribution: ['quarterlyChart', 'bar', 'تعداد', { stacked: true }],
            sprintVelocity: ['velocityChart', 'bar', 'تعداد'],
            reworkRate: ['reworkChart', 'line', 'درصد']
        };

        results.forEach((result, i) => {
            if (i >= names.length) return;
            const name = names[i];
            const [id, type, title, opts = {}] = chartInfo[name];
            if (result.status === 'rejected') {
                failures++;
                showChartError(id, `دریافت ناموفق: ${result.reason?.message ?? 'خطای ناشناخته'}`);
                return;
            }

            data[name] = result.value;
            try {
                draw(id, result.value, type, title, opts);
                if (name === 'quarterlyDistribution') updateQuarterlyInsight(result.value);
                showChartError(id, '');
            } catch (error) {
                failures++;
                showChartError(id, error?.message || 'خطا در رسم نمودار');
            }
        });

        if (!renderIndividualPerformance(results[names.length])) failures++;

        updateKpis(data);
        await loadWorklogReport();

        const project = $('#selectedProject'); if (project) project.textContent = projectKey() || '—';
        const updated = $('#updatedAt'); if (updated) updated.textContent = new Date().toLocaleTimeString('fa-IR');

        if (failures && alert) {
            alert.textContent = `بارگذاری ${failures} گزارش با خطا مواجه شد؛ وضعیت هر نمودار جداگانه مشخص شده است.`;
            alert.hidden = false;
        }
        if (overlay) overlay.hidden = true;
    }

    async function loadWorklogUsers() {
        const select = $('#worklogUserFilter');
        if (!select) return;
        const prior = select.value;
        try {
            const users = await fetchJson(makeUrl(WORKLOG_USERS_ENDPOINT, ''));
            select.innerHTML = '<option value="">همه کاربران</option>';
            users.forEach(user => {
                const option = document.createElement('option');
                option.value = user.accountId;
                option.textContent = user.displayName;
                select.appendChild(option);
            });
            if ([...select.options].some(option => option.value === prior)) select.value = prior;
        } catch (error) { console.warn('فهرست کاربران worklog دریافت نشد:', error.message); }
    }

    async function loadWorklogReport() {
        const url = new URL(makeUrl('/api/reports/worklogs'));
        const user = $('#worklogUserFilter')?.value;
        if (user) url.searchParams.set('accountId', user);
        try {
            const report = await fetchJson(url);
            setKpi('worklog-total-hours', Number(report.totalHours || 0).toFixed(2), ' ساعت');
            setKpi('worklog-count', report.worklogCount);
            const daily = report.daily || [];
            const canvas = $('#worklogDailyChart');
            if (canvas && window.Chart) {
                if (charts.has('worklogDailyChart')) charts.get('worklogDailyChart').destroy();
                const byDay = new Map();
                daily.forEach(row => byDay.set(row.day, (byDay.get(row.day) || 0) + Number(row.hours || 0)));
                const chart = new Chart(canvas, { type: 'bar', data: { labels: [...byDay.keys()], datasets: [{ label: 'ساعت', data: [...byDay.values()], backgroundColor: colors[0] }] }, options: { responsive: true, maintainAspectRatio: false, scales: { x: { grid: { color: '#24334a' } }, y: { beginAtZero: true, grid: { color: '#24334a' } } }, plugins: { legend: { display: false }, tooltip: { rtl: true } } } });
                charts.set('worklogDailyChart', chart);
                showChartError('worklogDailyChart', daily.length ? '' : 'داده‌ای برای نمایش وجود ندارد.');
            }
            renderWorklogTable('worklogPeople', report.people, ['person', 'hours', 'worklogCount'], ['کاربر', 'ساعت', 'ثبت‌ها']);
            renderWorklogTable('worklogIssues', report.issues, ['issueKey', 'summary', 'hours'], ['Issue', 'عنوان', 'ساعت']);
        } catch (error) {
            showChartError('worklogDailyChart', `دریافت ناموفق: ${error.message}`);
        }
    }

    function renderWorklogTable(id, rows, keys, headings) {
        const target = document.getElementById(id);
        if (!target) return;
        const table = document.createElement('table'); table.className = 'table worklog-table table-sm align-middle mb-0';
        const head = table.createTHead().insertRow();
        headings.forEach((label, index) => { const cell = document.createElement('th'); cell.textContent = label; cell.className = `worklog-col-${keys[index]}`; head.appendChild(cell); });
        const body = table.createTBody();
        (rows || []).forEach(row => { const tr = body.insertRow(); keys.forEach(key => { const cell = tr.insertCell(); cell.className = `worklog-col-${key}`; cell.textContent = row[key] ?? '—'; }); });
        if (!rows?.length) { const tr = body.insertRow(); const cell = tr.insertCell(); cell.colSpan = headings.length; cell.textContent = 'داده‌ای موجود نیست.'; }
        target.replaceChildren(table);
    }

    async function handleProjectChange() {
        await loadSprintsList();
        await loadDashboard();
    }

    async function sync() {
        const button = $('#syncButton'), alert = $('#dashboardError');
        if (button) button.disabled = true;
        try {
            await fetchJson(makeUrl('/api/sync', ''), { method: 'POST' });
            await loadSprintsList();
            await loadWorklogUsers();
            await loadDashboard();
        } catch (error) {
            if (alert) {
                alert.textContent = `همگام‌سازی ناموفق: ${error.message}`;
                alert.hidden = false;
            }
        } finally {
            if (button) button.disabled = false;
        }
    }

    document.addEventListener('DOMContentLoaded', async () => {
        const input = $('#projectKeyInput'), selected = $('#selectedProject');
        const sprintSelect = $('#sprintFilter');

        if (input) {
            input.addEventListener('change', handleProjectChange);
            input.addEventListener('keydown', event => { if (event.key === 'Enter') handleProjectChange(); });
        }

        if (sprintSelect) {
            sprintSelect.addEventListener('change', loadDashboard);
        }
        $('#worklogUserFilter')?.addEventListener('change', loadWorklogReport);

        if (selected) selected.textContent = projectKey() || '—';
        $('#syncButton')?.addEventListener('click', sync);

        await loadSprintsList();
        await loadWorklogUsers();
        await loadDashboard();
    });
})();
