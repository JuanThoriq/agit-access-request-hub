# Access Request Hub

MVP single source of truth untuk **access request**, **approval berlapis**, dan **audit trail** — dibangun sebagai
vertical slice end-to-end: ASP.NET Core (.NET 8) + EF Core + SQLite + React (Vite, TypeScript).

> Status: Phase 1 (scope sesuai assessment). Phase 2 (change request) belum dan tidak diimplementasikan.

## Quick start

Prasyarat: .NET SDK 8, Node.js 20+.

```bash
# 1. Backend (terminal 1) -> http://localhost:5080
cd src/AccessRequestHub.Api
dotnet run
# Database SQLite dibuat otomatis di src/AccessRequestHub.Api/data/access-request-hub.db
# (migration + seed dijalankan saat startup, idempoten)

# 2. Frontend (terminal 2) -> http://localhost:5173
cd web
npm install
npm run dev
```

Buka http://localhost:5173, pilih demo user di user switcher.

Dokumentasi lengkap menyusul di bagian bawah file ini setelah implementasi selesai.
