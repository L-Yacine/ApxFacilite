// Collapsible sidebar (desktop rail) + mobile bottom-nav "Plus" sheet
(function () {
  var root = document.body;

  var sidebar = document.getElementById('appSidebar');
  var handle = document.getElementById('sidebarCollapse');
  if (sidebar && handle) {
    var STORAGE_KEY = 'apxemi.sidebar.collapsed';

    function setCollapsed(collapsed, persist) {
      root.classList.toggle('sidebar-collapsed', collapsed);
      handle.setAttribute('aria-expanded', String(!collapsed));
      if (persist) {
        try {
          localStorage.setItem(STORAGE_KEY, collapsed ? '1' : '0');
        } catch (e) { /* stockage indisponible */ }
      }
    }

    handle.addEventListener('click', function () {
      setCollapsed(!root.classList.contains('sidebar-collapsed'), true);
    });

    try {
      if (localStorage.getItem(STORAGE_KEY) === '1') {
        setCollapsed(true, false);
      }
    } catch (e) { /* stockage indisponible */ }
  }

  var moreBtn = document.getElementById('bottomMore');
  var sheet = document.getElementById('bottomSheet');
  var backdrop = document.getElementById('bottomSheetBackdrop');
  var sheetClose = document.getElementById('bottomSheetClose');
  var sheetOpen = false;

  function setSheet(open) {
    sheetOpen = open;
    root.classList.toggle('bottom-sheet-open', open);
    if (moreBtn) {
      moreBtn.setAttribute('aria-expanded', String(open));
    }
    if (sheet) {
      sheet.setAttribute('aria-hidden', String(!open));
    }
  }

  if (moreBtn && sheet && backdrop) {
    moreBtn.addEventListener('click', function () {
      setSheet(!sheetOpen);
    });

    backdrop.addEventListener('click', function () {
      setSheet(false);
    });

    if (sheetClose) {
      sheetClose.addEventListener('click', function () {
        setSheet(false);
      });
    }

    sheet.addEventListener('click', function (e) {
      if (e.target.closest('a, button')) {
        setSheet(false);
      }
    });
  }

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
      setSheet(false);
    }
  });
})();