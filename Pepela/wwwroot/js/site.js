(function () {
    document.querySelectorAll('input[type="checkbox"]').forEach(function (checkbox) {
        checkbox.addEventListener('change', function (e) {
            let changedBox = this;
            document.getElementsByName(this.name).forEach(function (elem) {
                if (elem !== changedBox) {
                    elem.checked = false;
                }
            });
        });
    });

    document.querySelectorAll('.timeslot-card').forEach(function (card) {
        card.addEventListener('click', function (e) {
            const targetTag = e.target.tagName.toLowerCase();
            if (targetTag === "input" || targetTag === "label") {
                return;
            }
            const targetInput = this.querySelector('input');
            targetInput.click();
        });
    });
})();
