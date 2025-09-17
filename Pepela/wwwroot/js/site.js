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

    function updatePubQuizInputs(checkbox) {
        document.querySelectorAll('.pubquiz-team-input').forEach(function (elem) {
            if (checkbox.checked) {
                elem.setAttribute('disabled', 'disabled');
                elem.setAttribute('readonly', 'readonly');
            } else {
                elem.removeAttribute('disabled');
                elem.removeAttribute('readonly');
            }
        });
    }

    document.querySelectorAll("#reserveQuizSoloSeat").forEach(function (checkbox) {
        checkbox.addEventListener('change', function (e) {
            updatePubQuizInputs(checkbox);
        });
    });

    document.addEventListener('DOMContentLoaded', function () {
        const checkbox = document.getElementById("reserveQuizSoloSeat");
        if (checkbox) {
            updatePubQuizInputs(checkbox);
        }
    });
})();
