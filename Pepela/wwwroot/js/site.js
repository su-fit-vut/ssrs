// Make all radios uncheckable
(function () {
    let lastCheckedRadio = null;
    document.querySelectorAll('input[type="radio"]').forEach(function (radio) {
        radio.addEventListener('click', function (e) {
            // If the clicked radio is the same as the last checked, uncheck it
            if (lastCheckedRadio === radio) {
                radio.checked = false;
                lastCheckedRadio = null;  // Reset the last checked radio
            } else {
                lastCheckedRadio = radio;  // Update last checked radio
            }
        });
    });
})();
