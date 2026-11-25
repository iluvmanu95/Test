const { mkdir, writeFile } = require("node:fs/promises");
const path = require("node:path");
const base = process.argv[2] ?? "http://localhost:5296";
const out = path.join(__dirname, "obj", "sql-load");
const rows = [60, 200];
const users = [1, 5, 10];
const waves = 5;

async function exportPdf(engine, count) {
    const start = performance.now();
    const response = await fetch(`${base}/reports/${engine}/${count}`, { signal: AbortSignal.timeout(30000) });
    const pdf = Buffer.from(await response.arrayBuffer());
    const elapsed = performance.now() - start;
    const header = name => response.headers.get(name);
    if (!response.ok || header("X-Data-Source") !== "sql" || header("X-Pdf-Empty-Signatures") !== "2" ||
        Number(header("X-Pdf-Pages")) !== (count === 60 ? 3 : 10) || pdf.subarray(0, 5).toString() !== "%PDF-") {
        throw new Error(`${engine}/${count}: HTTP ${response.status}, source ${header("X-Data-Source")}, signatures ${header("X-Pdf-Empty-Signatures")}`);
    }
    return { elapsed, sql: Number(header("X-Data-Ms")), render: Number(header("X-Export-Ms")), pdf };
}

function percentile(values, p) {
    const sorted = [...values].sort((a, b) => a - b);
    return sorted[Math.ceil(p * sorted.length) - 1]?.toFixed(2) ?? "";
}

async function main() {
    await mkdir(out, { recursive: true });
    const results = [];
    for (const count of rows) {
        for (const engine of ["fast", "bold"]) {
            const { pdf } = await exportPdf(engine, count);
            await writeFile(path.join(out, `${engine}-${count}.pdf`), pdf);
        }
        for (const concurrency of users) {
            const samples = { fast: [], bold: [] };
            const wall = { fast: 0, bold: 0 };
            for (let repeat = 0; repeat < waves; repeat++) {
                for (const engine of repeat % 2 ? ["bold", "fast"] : ["fast", "bold"]) {
                    const start = performance.now();
                    const batch = await Promise.allSettled(Array.from({ length: concurrency }, () => exportPdf(engine, count)));
                    wall[engine] += performance.now() - start;
                    samples[engine].push(...batch);
                }
            }
            for (const engine of ["fast", "bold"]) {
                const all = samples[engine];
                const ok = all.filter(item => item.status === "fulfilled").map(item => item.value);
                for (const failure of all.filter(item => item.status === "rejected"))
                    console.error(failure.reason.message);
                results.push({
                    rows: count, engine, concurrency, ok: ok.length, errors: all.length - ok.length,
                    p50Ms: percentile(ok.map(item => item.elapsed), .5),
                    p95Ms: percentile(ok.map(item => item.elapsed), .95),
                    sqlP50Ms: percentile(ok.map(item => item.sql), .5),
                    exportP50Ms: percentile(ok.map(item => item.render), .5),
                    pdfPerSecond: (ok.length * 1000 / wall[engine]).toFixed(2)
                });
            }
        }
    }
    const keys = Object.keys(results[0]);
    await writeFile(path.join(out, "benchmark.csv"), [keys.join(","), ...results.map(result => keys.map(key => result[key]).join(","))].join("\n") + "\n");
    console.table(results);
    if (results.some(result => result.errors)) process.exitCode = 1;
}

main().catch(error => { console.error(error); process.exitCode = 1; });
