// EyewaysMergeSafeServer — site.js

document.addEventListener('DOMContentLoaded', function () {
    var toggle = document.getElementById('sidebarToggle');
    var sidebar = document.querySelector('.mss-sidebar');
    if (toggle && sidebar) {
        toggle.addEventListener('click', function () {
            sidebar.classList.toggle('open');
        });
        document.addEventListener('click', function (e) {
            if (window.innerWidth < 768 && sidebar.classList.contains('open')) {
                if (!sidebar.contains(e.target) && e.target !== toggle) {
                    sidebar.classList.remove('open');
                }
            }
        });
    }

    setTimeout(function () {
        document.querySelectorAll('.alert.alert-success').forEach(function (el) {
            var bsAlert = bootstrap.Alert.getOrCreateInstance(el);
            bsAlert.close();
        });
    }, 4000);

    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(function (el) {
        new bootstrap.Tooltip(el);
    });
});

function startLiveClock(elementId) {
    function tick() {
        var el = document.getElementById(elementId);
        if (!el) return;
        el.textContent = new Date().toLocaleTimeString();
    }
    tick();
    setInterval(tick, 1000);
}
