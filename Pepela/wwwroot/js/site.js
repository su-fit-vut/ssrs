(function () {
    document.querySelectorAll('input[type="checkbox"]').forEach(function (checkbox) {
        checkbox.addEventListener('change', function (e) {
            let changedBox = this;
            
            document.getElementsByName(this.name).forEach(function (elem) {
                if (elem !== changedBox) {
                    elem.checked = false;
                }
            });
            
            console.info("change: " + this.name + " to " + this.checked);
        });
        
        checkbox.addEventListener('click', function (e) {
        });
    });
})();
