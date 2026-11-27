document.addEventListener("input", event => {
    const input = event.target;
    if (input.id !== "report-rows") return;

    if (input.validity.rangeOverflow) input.value = input.max;

    showContractPage(input, 1);
});

document.addEventListener("click", event => {
    if (event.target.closest("#preview-designed-report")) {
        showReportPreview();
        return;
    }
    const button = event.target.closest("#sql-contract-pagination button");
    if (button) showContractPage(document.getElementById("report-rows"), Number(button.value));
});

function showReportPreview() {
    const input = document.getElementById("report-rows");
    if (!input.reportValidity()) return;

    const table = document.getElementById("sql-contracts");
    const count = Math.min(input.valueAsNumber, table.tBodies[0].rows.length);
    const status = document.getElementById("designed-report-status");
    const preview = window.open("about:blank", "_blank");
    if (!preview) {
        status.textContent = "Trình duyệt đã chặn tab xem trước. Hãy cho phép mở tab mới.";
        return;
    }

    status.textContent = "";
    preview.opener = null;
    const view = preview.document;
    view.documentElement.lang = "vi";
    view.title = "Xem trước báo cáo";
    const styling = view.createElement("style");
    styling.textContent = "body{margin:24px;font:16px system-ui}a{display:inline-block;margin-bottom:16px;padding:8px 18px;background:#0b4e5c;color:white;border-radius:4px}table{border-collapse:collapse}caption{text-align:left;margin-bottom:8px}th,td{border:1px solid #ccc;padding:8px;text-align:left}";
    view.head.append(styling);

    const print = view.createElement("a");
    print.href = new URL(`/reports/print/${count}`, window.location.origin).href;
    print.textContent = "In";

    const report = table.cloneNode(true);
    report.caption.textContent = `Báo cáo hợp đồng · ${count} dòng`;
    [...report.tBodies[0].rows].forEach((row, index) => {
        if (index < count) row.hidden = false;
        else row.remove();
    });
    view.body.append(print, report);
}

function showContractPage(input, page) {
    const table = document.getElementById("sql-contracts");
    if (!table) return;

    const pageSize = 5;
    const rows = table.tBodies[0].rows;
    const count = input.validity.valid ? Math.min(input.valueAsNumber, rows.length) : 0;
    const pageCount = Math.max(1, Math.ceil(count / pageSize));
    page = Math.min(Math.max(page, 1), pageCount);
    const first = (page - 1) * pageSize;
    const end = Math.min(first + pageSize, count);
    for (let index = 0; index < rows.length; index++) {
        rows[index].hidden = index < first || index >= end;
    }
    document.getElementById("sql-contract-count").textContent = end - first;

    const pagination = document.getElementById("sql-contract-pagination");
    pagination.hidden = pageCount === 1;
    const [previous, next] = pagination.querySelectorAll("button");
    previous.disabled = page === 1;
    previous.value = page - 1;
    next.disabled = page === pageCount;
    next.value = page + 1;
    document.getElementById("sql-contract-page").textContent = `Trang ${page}/${pageCount}`;
}
