/**
 * ALOBALL EA FC 27 PLAYSTYLES & UI CONTROLLER
 */
(function () {
    let cachedCatalog = null;
    let activePlayerId = null;
    let activePlayerName = '';
    let currentSelection = {}; // { [code]: 'regular' | 'plus' }

    // 1. Get or create modal
    function getOrCreatePlayStyleModal() {
        let el = document.getElementById('fc27PlayStyleModal');
        if (!el) {
            const html = `
                <div class="modal fade fc27-modal" id="fc27PlayStyleModal" tabindex="-1" aria-hidden="true">
                    <div class="modal-dialog modal-dialog-centered modal-lg">
                        <div class="modal-content">
                            <div class="modal-header d-flex justify-content-between align-items-center">
                                <div>
                                    <div class="d-flex align-items-center gap-2">
                                        <span class="badge" style="background: linear-gradient(135deg, #a3e635 0%, #4ade80 100%); color: #090d16; font-weight: 800;">FC 27 PLAYSTYLES</span>
                                        <h5 class="modal-title fw-bold text-white mb-0" id="fc27PsModalTitle">Gán Kỹ Năng Cầu Thủ</h5>
                                    </div>
                                    <small class="text-muted d-block mt-1">1 lần bấm: <strong style="color: #38bdf8;">PlayStyle Thường</strong> · 2 lần bấm: <strong style="color: #f59e0b;">PlayStyle+ (Gold)</strong> · 3 lần: Bỏ chọn</small>
                                </div>
                                <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal"></button>
                            </div>
                            <div class="modal-body p-3">
                                <!-- Counter Bar -->
                                <div class="d-flex justify-content-between align-items-center p-2 rounded mb-3" style="background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.08);">
                                    <div class="d-flex align-items-center gap-2">
                                        <span class="small text-muted">Đã chọn:</span>
                                        <span class="badge" id="fc27PlusCounterBadge" style="background: linear-gradient(135deg, #f59e0b 0%, #fef08a 100%); color: #000; font-weight: 800;">0/1 PlayStyle+</span>
                                        <span class="badge" id="fc27RegCounterBadge" style="background: linear-gradient(135deg, #38bdf8 0%, #e2e8f0 100%); color: #000; font-weight: 800;">0/3 Thường</span>
                                    </div>
                                    <button type="button" class="btn btn-sm btn-outline-secondary" onclick="window.fc27ClearAllSelection()" style="font-size: 0.75rem;">Xóa tất cả</button>
                                </div>

                                <!-- Category Tabs -->
                                <ul class="nav nav-pills gap-1 mb-3" id="fc27PsCategoryTabs" role="tablist"></ul>

                                <!-- Tab Content -->
                                <div class="tab-content" id="fc27PsCategoryContent"></div>
                            </div>
                            <div class="modal-footer border-top-0 d-flex justify-content-between">
                                <span class="small text-muted" id="fc27PsStatusMsg"></span>
                                <div class="d-flex gap-2">
                                    <button type="button" class="btn btn-outline-secondary btn-sm" data-bs-dismiss="modal">Đóng</button>
                                    <button type="button" class="btn btn-sm fw-bold px-3" id="fc27BtnSavePlayStyles" onclick="window.fc27SavePlayStyles()" style="background: #a3e635; color: #090d16;">
                                        <i class="bi bi-check2-circle me-1"></i>Lưu Kỹ Năng
                                    </button>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            `;
            document.body.insertAdjacentHTML('beforeend', html);
            el = document.getElementById('fc27PlayStyleModal');
        }
        return new bootstrap.Modal(el);
    }

    // 2. Load catalog
    async function fetchCatalog() {
        if (cachedCatalog) return cachedCatalog;
        try {
            const res = await fetch('/api/playstyles/catalog');
            if (res.ok) {
                cachedCatalog = await res.json();
                return cachedCatalog;
            }
        } catch (e) {
            console.error('Failed to load PlayStyles catalog', e);
        }
        return null;
    }

    // 3. Open modal trigger
    window.openPlayStyleModal = async function (playerId, playerName, rawPlayStyles) {
        activePlayerId = playerId;
        activePlayerName = playerName || 'Cầu thủ';
        currentSelection = {};

        if (rawPlayStyles) {
            const tokens = rawPlayStyles.split(',');
            tokens.forEach(t => {
                t = t.trim();
                if (!t) return;
                if (t.endsWith('+')) {
                    currentSelection[t.slice(0, -1).toUpperCase()] = 'plus';
                } else {
                    currentSelection[t.toUpperCase()] = 'regular';
                }
            });
        }

        const modal = getOrCreatePlayStyleModal();
        document.getElementById('fc27PsModalTitle').textContent = `Gán Kỹ Năng · ${activePlayerName}`;
        document.getElementById('fc27PsStatusMsg').textContent = '';

        const catalog = await fetchCatalog();
        if (catalog && catalog.categories) {
            renderModalContent(catalog.categories);
        }
        updateCounters();
        modal.show();
    };

    function renderModalContent(categories) {
        const tabsEl = document.getElementById('fc27PsCategoryTabs');
        const contentEl = document.getElementById('fc27PsCategoryContent');
        tabsEl.innerHTML = '';
        contentEl.innerHTML = '';

        categories.forEach((cat, idx) => {
            const tabId = `fc27-tab-${idx}`;
            const paneId = `fc27-pane-${idx}`;
            const isActive = idx === 0;

            // Tab button
            const tabBtn = document.createElement('li');
            tabBtn.className = 'nav-item';
            tabBtn.innerHTML = `
                <button class="nav-link ${isActive ? 'active' : ''}" id="${tabId}" data-bs-toggle="pill" data-bs-target="#${paneId}" type="button">
                    ${cat.category} (${cat.skills.length})
                </button>
            `;
            tabsEl.appendChild(tabBtn);

            // Tab pane
            const pane = document.createElement('div');
            pane.className = `tab-pane fade ${isActive ? 'show active' : ''}`;
            pane.id = paneId;

            const grid = document.createElement('div');
            grid.className = 'fc27-ps-grid';

            cat.skills.forEach(skill => {
                const card = document.createElement('div');
                card.className = 'fc27-ps-card';
                card.id = `fc27-card-${skill.code}`;
                card.onclick = () => onSkillCardClicked(skill.code);

                updateCardElement(card, skill);
                grid.appendChild(card);
            });

            pane.appendChild(grid);
            contentEl.appendChild(pane);
        });
    }

    function updateCardElement(card, skill) {
        const state = currentSelection[skill.code] || 'none';
        card.classList.remove('ps-selected-regular', 'ps-selected-plus');

        let diamondClass = 'fc27-ps-cyan';
        let badgeHtml = '';

        if (state === 'plus') {
            card.classList.add('ps-selected-plus');
            diamondClass = 'fc27-ps-gold';
            badgeHtml = '<span class="fc27-ps-badge-pill fc27-badge-plus">+ GOLD</span>';
        } else if (state === 'regular') {
            card.classList.add('ps-selected-regular');
            diamondClass = 'fc27-ps-cyan';
            badgeHtml = '<span class="fc27-ps-badge-pill fc27-badge-reg">PLAYSTYLE</span>';
        }

        card.innerHTML = `
            ${badgeHtml}
            <div class="card-diamond">
                <span class="fc27-ps-diamond ${diamondClass}">
                    <svg viewBox="0 0 24 24" class="fc27-ps-icon"><path d="${skill.svgPath}" fill="currentColor" /></svg>
                </span>
            </div>
            <div class="card-name-vi">${skill.nameVi}</div>
            <div class="card-name-en">${skill.nameEn}</div>
        `;
    }

    function onSkillCardClicked(code) {
        const current = currentSelection[code] || 'none';
        const plusCount = Object.values(currentSelection).filter(v => v === 'plus').length;
        const regCount = Object.values(currentSelection).filter(v => v === 'regular').length;

        if (current === 'none') {
            if (regCount < 3) {
                currentSelection[code] = 'regular';
            } else if (plusCount < 1) {
                currentSelection[code] = 'plus';
            } else {
                showToast('Đã đạt giới hạn 3 PlayStyle thường và 1 PlayStyle+.');
                return;
            }
        } else if (current === 'regular') {
            if (plusCount < 1) {
                currentSelection[code] = 'plus';
            } else {
                delete currentSelection[code];
            }
        } else if (current === 'plus') {
            delete currentSelection[code];
        }

        // Rerender all skill cards to reflect classes
        if (cachedCatalog && cachedCatalog.categories) {
            for (const cat of cachedCatalog.categories) {
                const skill = cat.skills.find(s => s.code === code);
                if (skill) {
                    const card = document.getElementById(`fc27-card-${code}`);
                    if (card) updateCardElement(card, skill);
                    break;
                }
            }
        }
        updateCounters();
    }

    function updateCounters() {
        const plusCount = Object.values(currentSelection).filter(v => v === 'plus').length;
        const regCount = Object.values(currentSelection).filter(v => v === 'regular').length;

        const plusEl = document.getElementById('fc27PlusCounterBadge');
        const regEl = document.getElementById('fc27RegCounterBadge');

        if (plusEl) plusEl.textContent = `${plusCount}/1 PlayStyle+`;
        if (regEl) regEl.textContent = `${regCount}/3 Thường`;
    }

    window.fc27ClearAllSelection = function () {
        currentSelection = {};
        if (cachedCatalog && cachedCatalog.categories) {
            cachedCatalog.categories.forEach(cat => {
                cat.skills.forEach(skill => {
                    const card = document.getElementById(`fc27-card-${skill.code}`);
                    if (card) updateCardElement(card, skill);
                });
            });
        }
        updateCounters();
    };

    window.fc27SavePlayStyles = async function () {
        if (!activePlayerId) return;

        const tokens = [];
        for (const [code, state] of Object.entries(currentSelection)) {
            if (state === 'plus') tokens.unshift(`${code}+`);
            else if (state === 'regular') tokens.push(code);
        }

        const rawResult = tokens.join(',');
        const btn = document.getElementById('fc27BtnSavePlayStyles');
        const originalText = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Đang lưu...';

        try {
            const antiForgeryToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
            const res = await fetch('/club/member/playstyles', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': antiForgeryToken
                },
                body: JSON.stringify({ playerId: activePlayerId, playStyles: rawResult })
            });

            const data = await res.json();
            if (!res.ok) throw new Error(data.message || 'Lỗi khi lưu PlayStyles.');

            // Update DOM clusters
            updateDomBadges(activePlayerId, data.playStyles);

            // Hide modal
            const modalEl = document.getElementById('fc27PlayStyleModal');
            const instance = bootstrap.Modal.getInstance(modalEl);
            if (instance) instance.hide();

            showToast(data.message || 'Đã lưu PlayStyles thành công!');
        } catch (err) {
            document.getElementById('fc27PsStatusMsg').innerHTML = `<span class="text-danger">${err.message}</span>`;
        } finally {
            btn.disabled = false;
            btn.innerHTML = originalText;
        }
    };

    function updateDomBadges(playerId, rawPlayStyles) {
        const inlineContainer = document.getElementById(`ps-inline-${playerId}`);
        const gridContainer = document.getElementById(`ps-grid-${playerId}`);

        const badgesHtml = buildBadgesHtml(rawPlayStyles);
        if (inlineContainer) inlineContainer.innerHTML = badgesHtml;
        if (gridContainer) gridContainer.innerHTML = badgesHtml;

        // Also update data attribute or onclick in assign buttons
        document.querySelectorAll(`.fc27-btn-assign-ps`).forEach(btn => {
            if (btn.getAttribute('onclick')?.includes(playerId)) {
                btn.setAttribute('onclick', `openPlayStyleModal('${playerId}', '${activePlayerName.replace(/'/g, "\\'")}', '${rawPlayStyles || ""}')`);
            }
        });

        // Initialize tooltips on new elements
        if (window.bootstrap && bootstrap.Tooltip) {
            document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(el => new bootstrap.Tooltip(el));
        }
    }

    function buildBadgesHtml(raw) {
        if (!raw || !cachedCatalog) return '';
        const tokens = raw.split(',').map(t => t.trim()).filter(Boolean);
        if (tokens.length === 0) return '';

        let html = '<span class="fc27-ps-cluster" aria-label="PlayStyles">';
        tokens.forEach(t => {
            const isPlus = t.endsWith('+');
            const code = t.replace('+', '').toUpperCase();
            let def = null;
            for (const cat of cachedCatalog.categories) {
                def = cat.skills.find(s => s.code === code);
                if (def) break;
            }
            if (!def) return;

            const diamondClass = isPlus ? 'fc27-ps-gold' : 'fc27-ps-cyan';
            const title = `${def.nameVi} (${def.nameEn})${isPlus ? ' ★ PlayStyle+' : ''}`;
            html += `
                <span class="fc27-ps-item" title="${title}">
                    <span class="fc27-ps-diamond ${diamondClass}">
                        <svg viewBox="0 0 24 24" class="fc27-ps-icon"><path d="${def.svgPath}" fill="currentColor" /></svg>
                    </span>
                    <span class="fc27-ps-hover-tooltip">${title}</span>
                </span>
            `;
        });
        html += '</span>';
        return html;
    }

    function showToast(msg) {
        const toast = document.createElement('div');
        toast.className = 'position-fixed bottom-0 end-0 p-3';
        toast.style.zIndex = '9999';
        toast.innerHTML = `
            <div class="toast align-items-center text-white border-0 show" style="background: #111827; border-left: 4px solid #a3e635 !important; box-shadow: 0 10px 25px rgba(0,0,0,0.5);">
                <div class="d-flex">
                    <div class="toast-body"><i class="bi bi-gem text-warning me-2"></i>${msg}</div>
                    <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>
                </div>
            </div>
        `;
        document.body.appendChild(toast);
        setTimeout(() => toast.remove(), 4000);
    }
})();
