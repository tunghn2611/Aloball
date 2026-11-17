(() => {
    'use strict';
    const $ = (s, r = document) => r.querySelector(s);
    const $$ = (s, r = document) => [...r.querySelectorAll(s)];

    // Mobile drawer
    const burger = $('.lp-burger'), drawer = $('#lpDrawer');
    const setDrawer = open => {
        burger.setAttribute('aria-expanded', open);
        burger.setAttribute('aria-label', open ? 'Đóng menu' : 'Mở menu');
        drawer.hidden = !open;
    };
    burger?.addEventListener('click', () => setDrawer(drawer.hidden));
    drawer?.addEventListener('click', e => { if (e.target.closest('a')) setDrawer(false); });
    document.addEventListener('keydown', e => { if (e.key === 'Escape') setDrawer(false); });

    // Interactive showcase tabs (with arrow-key support)
    $$('[data-tabs]').forEach(root => {
        const tabs = $$('[role=tab]', root), panels = $$('[role=tabpanel]', root);
        const select = i => {
            tabs.forEach((t, n) => {
                const on = n === i;
                t.classList.toggle('is-active', on);
                t.setAttribute('aria-selected', on);
                t.tabIndex = on ? 0 : -1;
                panels[n].hidden = !on;
                panels[n].classList.toggle('is-active', on);
            });
        };
        tabs.forEach((t, i) => {
            t.addEventListener('click', () => select(i));
            t.addEventListener('keydown', e => {
                const d = { ArrowDown: 1, ArrowRight: 1, ArrowUp: -1, ArrowLeft: -1 }[e.key];
                if (!d) return;
                e.preventDefault();
                const n = (i + d + tabs.length) % tabs.length;
                select(n);
                tabs[n].focus();
            });
        });
    });

    // Hero attendance mockup: toggle updates counters and progress bar
    const att = $('[data-attendance]');
    if (att) {
        const total = 16, counts = { in: 14, out: 2 };
        const bar = $('.lp-progress', att);
        att.addEventListener('click', e => {
            const btn = e.target.closest('button[data-state]');
            if (!btn) return;
            const was = $('button.is-on', att)?.dataset.state;
            if (was === btn.dataset.state) return;
            counts[was]--; counts[btn.dataset.state]++;
            $$('button', att).forEach(b => b.classList.toggle('is-on', b === btn));
            $$('[data-count]', att).forEach(c => c.textContent = counts[c.dataset.count]);
            bar.firstElementChild.style.width = (counts.in / total * 100) + '%';
            bar.setAttribute('aria-valuenow', counts.in);
        });
    }

    // Sticky nav elevation + PWA service worker
    const nav = $('#lpNav');
    const onScroll = () => nav.style.boxShadow = scrollY > 8 ? '0 4px 20px rgba(15,23,42,.08)' : 'none';
    addEventListener('scroll', onScroll, { passive: true }); onScroll();
    if ('serviceWorker' in navigator) addEventListener('load', () => navigator.serviceWorker.register('/sw.js').catch(() => {}));
})();
