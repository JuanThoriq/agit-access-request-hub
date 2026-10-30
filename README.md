# Access Request Hub

MVP **single source of truth** untuk permintaan akses aplikasi internal: satu tempat untuk request,
approval berlapis, dan audit trail — menggantikan alur email/chat yang tidak terlacak.

Dibangun sebagai satu **vertical slice end-to-end**: create → approve/reject → audit.
Fokusnya correctness dan engineering quality, bukan jumlah fitur.

| Lapisan | Teknologi |
|---|---|
| Frontend | React 18 + TypeScript + Vite (SPA) |
| Backend | ASP.NET Core .NET 8, Minimal API |
| Database | SQLite + EF Core 8 (migration + seed reproducible) |
| Test | xUnit, 31 integration test lewat HTTP |

> Status: **Phase 1 selesai** (tag `phase-1-complete`). Phase 2 tidak dimulai karena dokumen
> Change Request belum diberikan.

---

## 1. Prasyarat

- .NET SDK 8 (atau 9 dengan target net8.0)
- Node.js 20+
- Tidak perlu server database — SQLite dibuat otomatis.

## 2. Menjalankan

Dua terminal.

**Terminal 1 — API** (`http://localhost:5080`):

```bash
cd src/AccessRequestHub.Api
dotnet run
```

Saat pertama kali jalan, aplikasi otomatis: membuat file `App_Data/access-request-hub.db`,
menjalankan migration, lalu seed demo users + master data. Aman dijalankan berulang (idempotent).

**Terminal 2 — Web** (`http://localhost:5173`):

```bash
cd web
npm install
npm run dev
```

Buka `http://localhost:5173`, pilih demo user di kanan atas.

> Vite mem-proxy `/api` ke `http://localhost:5080`, jadi browser tidak butuh CORS.
> Kalau port 5173 sudah dipakai proyek lain, Vite otomatis pindah ke 5174 — proxy tetap bekerja.

## 3. Database: reset, migrate, seed

```bash
# Reset total (hapus file, lalu jalankan ulang API: migrate + seed otomatis)
rm -f src/AccessRequestHub.Api/App_Data/access-request-hub.db*

# Hanya membuat/menjalankan migration (opsional, biasanya sudah otomatis saat start)
cd src/AccessRequestHub.Api && dotnet ef database update

# Menambah migration baru setelah mengubah model
cd src/AccessRequestHub.Api && dotnet ef migrations add NamaPerubahan -o Data/Migrations
```

File database tidak di-commit. Skema selalu berasal dari `src/AccessRequestHub.Api/Data/Migrations/`
ditambah seed di `Data/DatabaseInitializer.cs`.

## 4. Test

```bash
dotnet test                      # dari root repo
dotnet test --filter WorkflowTests    # satu kelas
dotnet test --filter FullyQualifiedName~StaleVersion_IsConflict
```

31 test integration, masing-masing memakai database SQLite sementara sendiri.
Pemetaan lengkap test → aturan bisnis ada di [`docs/07-TESTING.md`](docs/07-TESTING.md).

## 5. Demo users

| Email | Peran |
|---|---|
| `alice@example.local` | Requester. Melapor ke Bob. |
| `bob@example.local` | Manager. Memproses manager approval for Alice, Carol, Dana, Erin. |
| `carol@example.local` | System Owner aplikasi **CRM**. |
| `dana@example.local` | System Owner aplikasi **Finance Portal**. |
| `erin@example.local` | Admin/Auditor. Melihat semua request dan audit trail, **bukan** approver. |

Tidak ada password: identitas disimulasikan lewat header `X-Demo-User` yang di-resolve server.
Master data: aplikasi `CRM` dan `Finance Portal`; environment `NonProduction`/`Production`;
access level `Read`/`Admin`; policy `v1`.

## 6. Demo flow (7 skenario)

| # | Skenario | Langkah | Hasil |
|---|---|---|---|
| 1 | Standard | login Alice → New request → CRM / NonProduction / Read → submit → login Bob → Inbox → Approve | `Approved` setelah manager |
| 2 | Production | Alice → CRM / **Production** / Read → Bob Approve | pindah ke `Waiting system owner` → Carol Approve → `Approved` |
| 3 | Admin access | Alice → **Finance Portal** / NonProduction / **Admin** → Bob Approve → Dana Approve | `Approved` (butuh dua approver) |
| 4 | Unauthorized approval | login Erin (admin) → coba approve request orang | **403** `not_assigned_approver` |
| 5 | Duplicate submit | tekan Submit dua kali / klik cepat | tetap **satu** request (`201` lalu `200` replay) |
| 6 | Concurrent action | dua tab, dua user approve pada versi sama | satu berhasil, lainnya **409** `version_conflict` |
| 7 | Rejected | Bob Reject tanpa alasan → ditolak; isi alasan → `Rejected` | alasan tersimpan di audit, request tidak bisa diproses lagi |

Verifikasi cepat tanpa UI ada di `scripts/smoke.sh` (butuh API jalan dan `curl`).

## 7. API

Semua endpoint butuh header `X-Demo-User: <email demo>`.

| Method | Path | Kegunaan |
|---|---|---|
| GET | `/health` | health check (tanpa identitas) |
| GET | `/api/me` | user aktif + peran + aplikasi yang dimiliki + direct reports |
| GET | `/api/applications` | master data untuk form |
| GET | `/api/requests` | request milik user aktif |
| GET | `/api/requests/all` | semua request — admin/auditor saja |
| GET | `/api/requests/{id}` | detail + audit timeline (requester, manager, owner, admin) |
| GET | `/api/inbox` | request yang menunggu aksi user aktif |
| POST | `/api/requests` | create, idempotent lewat `clientRequestId` |
| POST | `/api/requests/{id}/approve` | body `{ "version": n }` |
| POST | `/api/requests/{id}/reject` | body `{ "version": n, "reason": "..." }` |

Contoh:

```bash
curl -s http://localhost:5080/api/requests \
  -H 'X-Demo-User: alice@example.local' -H 'Content-Type: application/json' \
  -d '{"clientRequestId":"demo-1","applicationId":1,"environment":"Production","accessLevel":"Read",
       "justification":"Need production read access for deploy verification"}'
```

### Error contract

Semua error adalah ProblemDetails (RFC 7807) dengan `code` yang stabil untuk dibaca UI:

| HTTP | `code` | Arti |
|---|---|---|
| 400 | `validation_failed` | field tidak valid; detail per field di `errors` |
| 401 | `missing_identity`, `unknown_demo_user` | header identitas tidak ada / bukan user demo |
| 403 | `not_a_participant`, `not_assigned_approver`, `self_approval_forbidden`, `admin_only` | tidak berhak |
| 404 | `request_not_found` | request tidak ada |
| 409 | `invalid_transition`, `version_conflict`, `idempotency_key_reuse`, `manager_not_configured` | bertentangan dengan state saat ini |

## 8. Struktur repo

```
src/AccessRequestHub.Api/
  Domain/        Models.cs (entity + enum), AccessWorkflow.cs (aturan bisnis murni)
  Data/          AppDbContext.cs, DatabaseInitializer.cs, Migrations/
  Api/           RequestEndpoints.cs, RequestMapper.cs, ApiProblems.cs, CurrentUserMiddleware.cs
  Contracts/     DTO request/response
tests/AccessRequestHub.Tests/
  TestSupport.cs (harness + helper HTTP), WorkflowTests, AuthorizationTests,
  ConcurrencyTests, AuditAndSchemaTests
web/src/         App.tsx, api.ts, types.ts, components/
docs/            materi belajar + persiapan interview
```

## 9. Dokumen

| File | Isi |
|---|---|
| `PLAN.md` | pemahaman masalah, desain, urutan implementasi, trade-off, perubahan plan |
| `AI_USAGE.md` | interaksi AI yang berpengaruh + **three things AI got wrong** |
| `REVIEW.md` | self review produksi: temuan, severity, status, evidence, deferred work |
| `INTEGRITY.md` | deklarasi ownership |
| [`docs/START-HERE.md`](docs/START-HERE.md) | index materi belajar (mulai dari sini) |
| `docs/01`–`docs/12` | requirement traceability, arsitektur, data model, workflow, concurrency, authorization, testing, trade-off, demo script, tanya-jawab, glosarium, skill/tools |

## 10. Bukti cepat bahwa semuanya jalan

```bash
dotnet build           # 0 warning, 0 error
dotnet test            # 31 passed
cd web && npm run build  # type-check + production build
```
