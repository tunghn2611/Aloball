(() => {
    const trigger = document.querySelector('.menu-trigger');
    const sidebar = document.querySelector('.arena-sidebar');
    const mobileMenu = window.matchMedia('(max-width: 991px)');
    function syncSidebar() {
        const hidden = mobileMenu.matches && !document.body.classList.contains('menu-open');
        if (sidebar) sidebar.inert = hidden;
        sidebar?.setAttribute('aria-hidden', String(hidden));
    }
    function setMenu(open) {
        document.body.classList.toggle('menu-open', open);
        trigger?.setAttribute('aria-expanded', String(open));
        syncSidebar();
        if (open) sidebar?.querySelector('a')?.focus();
        else trigger?.focus();
    }
    trigger?.addEventListener('click', () => setMenu(!document.body.classList.contains('menu-open')));
    document.querySelectorAll('.sidebar-close,.sidebar-scrim').forEach(button => button.addEventListener('click', () => setMenu(false)));
    document.addEventListener('keydown', event => {
        if (!document.body.classList.contains('menu-open')) return;
        if (event.key === 'Escape') setMenu(false);
        if (event.key === 'Tab') {
            const items = [...sidebar.querySelectorAll('a,button')].filter(el => el.offsetParent !== null);
            const first = items[0], last = items[items.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
            if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
        }
    });
    sidebar?.querySelectorAll('a').forEach(link => link.addEventListener('click', () => {
        document.body.classList.remove('menu-open');
        trigger?.setAttribute('aria-expanded', 'false');
        syncSidebar();
    }));
    mobileMenu.addEventListener('change', () => {
        document.body.classList.remove('menu-open');
        trigger?.setAttribute('aria-expanded', 'false');
        syncSidebar();
    });
    syncSidebar();
    document.querySelectorAll('i.bi').forEach(icon => icon.setAttribute('aria-hidden', 'true'));
    document.querySelectorAll('.btn-close:not([aria-label])').forEach(button => button.setAttribute('aria-label', 'Đóng'));
    if ('serviceWorker' in navigator) window.addEventListener('load', () => navigator.serviceWorker.register('/sw.js').catch(() => {}));
})();
