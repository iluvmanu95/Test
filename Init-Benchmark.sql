IF DB_ID(N'TestReportBenchmark') IS NULL
    CREATE DATABASE TestReportBenchmark;
GO
USE TestReportBenchmark;
GO
IF OBJECT_ID(N'dbo.Contracts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Contracts (
        SoThuTu int PRIMARY KEY, MaNhanVien nvarchar(10) NOT NULL,
        HoTen nvarchar(100) NOT NULL, PhongBan nvarchar(100) NOT NULL,
        SoHopDong nvarchar(30) NOT NULL, LoaiHopDong nvarchar(50) NOT NULL,
        NgayHieuLuc date NOT NULL, NgayHetHan date NULL, MucLuong decimal(18, 0) NOT NULL
    );
END;

INSERT dbo.Contracts
SELECT n, CONCAT(N'NV', suffix),
    CHOOSE((n - 1) % 5 + 1, N'Nguyễn Thị Minh Anh', N'Trần Quốc Bảo', N'Lê Hoàng Yến', N'Phạm Đình Khôi', N'Đỗ Ngọc Ánh'),
    CHOOSE((n - 1) % 4 + 1, N'Hành chính – Nhân sự', N'Kế toán', N'Vận hành', N'Quản lý chất lượng và Đào tạo nội bộ'),
    CONCAT(N'HĐLĐ-', suffix, N'/2026'),
    CASE WHEN n % 3 = 0 THEN N'Không xác định thời hạn' ELSE N'Xác định thời hạn' END,
    effectiveDate, CASE WHEN n % 3 <> 0 THEN DATEADD(day, -1, DATEADD(year, 1, effectiveDate)) END,
    18000000 + n * 500000
FROM GENERATE_SERIES(1, 200) AS numbers
CROSS APPLY (VALUES (numbers.value, RIGHT(CONCAT('00', numbers.value), 3),
    DATEADD(day, numbers.value - 1, DATEFROMPARTS(2026, 11, 1)))) AS sample(n, suffix, effectiveDate)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Contracts AS existing WHERE existing.SoThuTu = n);
GO
SELECT COUNT(*) AS BenchmarkContracts FROM dbo.Contracts;
