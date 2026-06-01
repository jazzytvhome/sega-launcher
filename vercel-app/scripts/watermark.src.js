(function () {
  var mark = document.getElementById("skid-mark");
  if (!mark) return;
  mark.classList.add("show");
  setTimeout(function () { mark.classList.remove("show"); }, 3000);
  setTimeout(function () { mark.remove(); }, 4000);
})();
