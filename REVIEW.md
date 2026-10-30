# REVIEW — self review production readiness

Ditinjau setelah Phase 1 selesai, dengan semua test hijau (`dotnet test` → 31 passed).
Kolom **Evidence** menunjuk test atau perintah yang membuktikan status, bukan opini.

## Temuan dan tindakan

| # | Area | Temuan | Severity | Action | Status | Evidence |
|---|------|--------|----------|--------|--------|----------|
| R-01 | Idempotency | Create memakai cek-lalu-insert; dua retry paralel bisa lolos cek dan menyimpan dua row | High | Ganti jaminan ke `UNIQUE (RequesterId, ClientRequestId)`; tangkap pelanggaran constraint lalu kembalikan row pemenang | Fixed | `ParallelDuplicateCreates_ProduceOneRow`, `DuplicateClientRequestId_ReturnsTheSameRequest` |
| R-02 | Concurrency | Approval tanpa penjaga versi: dua aksi pada data yang sama bisa dua-duanya "berhasil" | High | `Version` sebagai concurrency token → `UPDATE ... WHERE Version = @v`; `DbUpdateConcurrencyException` → 409 | Fixed | `TwoActionsOnTheSameVersion_ExactlyOneSucceeds`, `StaleVersion_IsConflict` |
| R-03 | Error handling | Semua kegagalan bisnis mengembalikan 400 sehingga UI tidak bisa membedakan sebab | Medium | ProblemDetails + `code`: 400 validation, 401 identity, 403 forbidden, 404 not found, 409 conflict | Fixed | `RejectWithoutReason_IsValidationError`, `AdminIsNotAnApprover`, `StaleVersion_IsConflict` |
| R-04 | Authorization | Approver yang kalah race menerima 403 "bukan approver" padahal masalahnya versi basi | Medium | Urutkan cek: participant → terminal → versi → approver → reason | Fixed | `TwoActionsOnTheSameVersion_ExactlyOneSucceeds` (loser harus 409) |
| R-05 | Correctness | Create sukses tetapi response 500 (navigation belum dimuat saat memetakan DTO) | High | Re-read dengan `Include` sebelum membentuk response | Fixed | smoke test `curl`, lalu seluruh test create |
| R-06 | Correctness | Jalur recovery duplikat melempar 500: entity anak tetap tracked setelah insert gagal | High | `ChangeTracker.Clear()` sebelum re-read | Fixed | `ParallelDuplicateCreates_ProduceOneRow` |
| R-07 | API contract | Error per field ter-flatten di root ProblemDetails, UI tidak bisa menandai input | Low | Nesting `errors` sesuai konvensi ProblemDetails | Fixed | `ShortJustification_AndUnknownApplication_AreValidationErrors` |
| R-08 | Audit | Tabel audit bisa diubah/dihapus oleh kode aplikasi (riwayat bisa ditulis ulang oleh bug) | Medium | Trigger SQLite menolak `UPDATE`/`DELETE` pada `AuditEvents` | Fixed | `AuditEvents_CannotBeUpdatedOrDeletedByApplicationCode` |
| R-09 | Data | SQLite tidak menyimpan `DateTimeKind`; JSON keluar tanpa `Z` sehingga jam di UI bergeser sesuai timezone | Medium | `DateTime.SpecifyKind(..., Utc)` di mapper response | Fixed | respons `/api/requests/{id}` sebelum/sesudah: `...T18:07:14` → `...T18:07:14Z` |
| R-10 | Workflow | Requester tanpa manajer bisa membuat request yang tidak akan pernah bisa di-approve | Medium | Tolak dengan 409 `manager_not_configured` | Fixed | `RequesterWithoutManager_CannotSubmitAnUnapprovableRequest` |
| R-11 | Scale | `/api/requests/all` tidak berpaginasi | Low | Deferred — volume demo kecil, UI memakai satu list | Accepted | `docs/08-TRADEOFFS.md` |
| R-12 | Abuse | Create/approve belum di-rate-limit | Low | Deferred — aplikasi lokal, tanpa auth nyata | Accepted | bagian *Deferred work* |
| R-13 | Privacy | Email user muncul di log request | Low | Accepted untuk lingkungan lokal; di produksi akan di-hash/di-mask | Accepted | `Program.cs` request logging |
| R-14 | Storage | SQLite hanya satu writer; `busy_timeout` 5000 ms menyerap burst kecil | Medium | Accepted dengan jalur upgrade ke PostgreSQL yang sudah ditulis | Accepted | `docs/02-ARSITEKTUR.md` |
| R-15 | XSS | Justifikasi dan alasan reject disimpan apa adanya | Low | Tidak berbahaya karena React meng-escape saat render; tidak ada `dangerouslySetInnerHTML` | Verified | `web/src/components/RequestDetail.tsx` |
| R-16 | CSRF | Tidak ada proteksi CSRF | Info | Tidak diperlukan: identitas lewat header `X-Demo-User`, bukan cookie, jadi browser tidak bisa mengirim kredensial otomatis | Verified | `web/src/api.ts`, `CurrentUserMiddleware.cs` |

## Yang sengaja tidak dikerjakan (deferred)

- **Phase 2** (change request dari assessor) — tidak boleh dimulai sebelum dokumennya diberikan.
- Pagination, pencarian, filter status; notifikasi email; komentar diskusi per request.
- RBAC administration UI, SSO nyata, provisioning otomatis, message broker, container/Kubernetes.
- Multi-node deployment: skema concurrency saat ini benar untuk satu database dengan banyak koneksi,
  bukan untuk banyak writer terdistribusi.

## Known limitations yang harus bisa dijelaskan

1. **Identitas disimulasikan.** `X-Demo-User` bisa diganti siapa saja — itu memang desain assessment.
   Yang tetap benar: server selalu resolve ke user yang ada di DB dan mengecek ulang izinnya.
2. **ClientRequestId unik per requester**, bukan global. Dua user berbeda boleh memakai key yang sama
   karena request-nya memang milik masing-masing.
3. **Tidak ada pembatalan request** oleh requester setelah submit — di luar scope Phase 1.
4. **Riwayat audit tidak bisa dikoreksi.** Salah tulis alasan tidak bisa diedit; koreksi akan
   ditambahkan sebagai event baru, bukan update (itu konsekuensi append-only).
5. **Migration dijalankan saat startup.** Nyaman untuk demo/reproducibility, tetapi di produksi
   seharusnya dijalankan sebagai langkah deploy terpisah dengan backup.

## Checklist produksi (ringkas)

| Item | Status |
|---|---|
| Tidak ada secret/kredensial di repo | Ya (`INTEGRITY.md`, `.gitignore`) |
| Konfigurasi lewat `appsettings`/env, bukan hardcode | Ya (`ConnectionStrings:Default`) |
| Database reproducible dari migration + seed | Ya (`DatabaseInitializer`), dibuktikan di README |
| Error tidak membocorkan stack trace | Ya (`UseExceptionHandler` → ProblemDetails generik) |
| Logging cukup untuk menelusuri request | Ya (method, path, status, durasi, user, traceId) |
| Health endpoint | Ya (`GET /health`) |
| Invariant kritis dijaga database | Ya (UNIQUE, CHECK, FK, trigger) |
| Perilaku kritis punya test | Ya (31 integration test) |
