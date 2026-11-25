document.addEventListener("click", async event => {
    const button = event.target.closest("#report-actions button");
    if (!button) return;

    const rows = document.getElementById("report-rows");
    if (!rows.reportValidity()) return;

    const count = rows.valueAsNumber;
    const engine = button.dataset.engine;
    const status = document.getElementById("report-status");
    const buttons = document.querySelectorAll("#report-actions button");
    buttons.forEach(item => item.disabled = true);
    status.textContent = "Đang xuất PDF…";
    try {
        const response = await fetch(`/reports/${engine}/${count}`);
        if (!response.ok) throw new Error(`HTTP ${response.status}`);

        const url = URL.createObjectURL(await response.blob());
        const download = document.createElement("a");
        download.href = url;
        download.download = `${engine}-hop-dong-${count}.pdf`;
        download.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);

        document.getElementById(`${engine}-rows`).textContent = count;
        document.getElementById(`${engine}-engine-ms`).textContent = response.headers.get("X-Engine-Ms");
        document.getElementById(`${engine}-ms`).textContent = response.headers.get("X-Export-Ms");
        document.getElementById(`${engine}-pages`).textContent = response.headers.get("X-Pdf-Pages");
        document.getElementById(`${engine}-signatures`).textContent = response.headers.get("X-Pdf-Empty-Signatures");
        status.textContent = "PDF đã sẵn sàng.";
    } catch (error) {
        status.textContent = `Xuất PDF thất bại (${error.message}).`;
    } finally {
        buttons.forEach(item => item.disabled = false);
    }
});

document.addEventListener("input", event => {
    const input = event.target;
    if (input.id !== "report-rows") return;

    if (input.validity.rangeOverflow) input.value = input.max;

    showContractPage(input, 1);
});

document.addEventListener("click", event => {
    const button = event.target.closest("#sql-contract-pagination button");
    if (button) showContractPage(document.getElementById("report-rows"), Number(button.value));
});

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
