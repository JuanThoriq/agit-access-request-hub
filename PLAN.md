# PLAN

Ditulis **sebelum** implementasi utama (lihat commit history: PLAN.md masuk sebelum domain code).
Bagian "Perubahan plan selama implementasi" diisi belakangan dan tidak dihapus — itu bagian dari evidence.

## 1. Problem understanding

Access request hari ini lewat email/chat. Tiga kegagalan proses yang harus hilang:

| Kegagalan hari ini | Akar masalah | Jawaban di desain ini |
|---|---|---|
| Approval sulit dilacak | state request hidup di inbox orang | status eksplisit di DB + audit event append-only |
| Request terkirim dua kali | retry email/klik ganda | `ClientRequestId` + UNIQUE index (database, bukan cek di aplikasi) |
| Dua approver bertindak pada request yang sama | tidak ada versioning | `Version` concurrency token, satu UPDATE menang |

Yang **bukan** masalah yang harus dijawab di sini: identitas nyata (SSO), notifikasi, skala, multi-tenant.
Karena itu semuanya di luar scope Phase 1 dan sengaja tidak dikerjakan.

Bentuk solusi: satu vertical slice — *create → approve/reject → audit* — yang benar, aman, reproducible,
bisa dijelaskan. Bukan aplikasi luas.

## 2. Arsitektur

```
┌──────────────────────────┐         ┌────────────────────────────────────────┐
│ web/  React SPA (Vite)   │  HTTP   │ src/AccessRequestHub.Api  (.NET 8)     │
│ - user switcher          │ ──────► │ Minimal API + endpoint filters         │
│ - create / inbox / detail│  JSON   │ Domain: AccessWorkflow (pure)          │
│ - conflict & error state │ ◄────── │ EF Core 8 + SQLite                     │
└──────────────────────────┘         └───────────────────┬────────────────────┘
                                                          │
                                            ┌─────────────▼─────────────┐
                                            │ SQLite (file)             │
                                            │ FK, UNIQUE, CHECK, trigger│
                                            └───────────────────────────┘
```

Lapisan (dan aturannya):

1. **Contracts** — DTO request/response. Tidak pernah dipakai sebagai entity.
2. **Domain** — `AccessWorkflow`: aturan bisnis murni (high-risk? transisi apa yang sah? siapa yang boleh?).
   Tanpa EF, tanpa HTTP → bisa dites tanpa database.
3. **Data** — `AppDbContext`, konfigurasi constraint, seed.
4. **Api** — endpoint tipis: resolve identity → validasi → panggil domain → simpan → map ke response.

**Trust boundary**: identity datang dari header `X-Demo-User` yang di-resolve server ke row `Users`.
Requester **tidak pernah** diambil dari body. Semua authorization dievaluasi di server per request;
menyembunyikan tombol di UI bukan kontrol keamanan.

## 3. Data model (ringkas)

```
Users(Id, Email UNIQUE, DisplayName, ManagerId → Users.Id, IsAdmin)
Applications(Id, Name UNIQUE, SystemOwnerId → Users.Id)
AccessRequests(
  Id, ClientRequestId, RequesterId → Users.Id, ApplicationId → Applications.Id,
  Environment, AccessLevel, Justification, Status, PolicyVersion, Version, CreatedAt, UpdatedAt
)
  UNIQUE (RequesterId, ClientRequestId)   -- idempotency tahan race
  CHECK Environment IN ('NonProduction','Production')
  CHECK AccessLevel IN ('Read','Admin')
  CHECK Status IN ('PendingManagerApproval','PendingSystemOwnerApproval','Approved','Rejected')
AuditEvents(Id, AccessRequestId → AccessRequests.Id, EventType, ActorUserId → Users.Id,
            FromStatus, ToStatus, Reason, CreatedAt)
  trigger: UPDATE/DELETE ditolak (append-only, tahan bug aplikasi)
```

Kenapa `Version` dipisah dari `UpdatedAt`: timestamp bukan pembanding yang aman (resolusi jam, dua update
di milidetik yang sama). Integer monotonik selalu bisa dibandingkan.

## 4. Workflow

```
PendingManagerApproval --approve(manager)--> high-risk? yes -> PendingSystemOwnerApproval
                                                        no  -> Approved
PendingManagerApproval --reject(manager, reason)--> Rejected
PendingSystemOwnerApproval --approve(owner)--> Approved
PendingSystemOwnerApproval --reject(owner, reason)--> Rejected
Approved | Rejected -> terminal (semua transition ditolak)
high-risk := Environment == Production OR AccessLevel == Admin
```

## 5. Implementation order

1. Repo, git, tag `assessment-start`, PLAN.md ← *sebelum coding besar*
2. Domain + entities + DbContext + migration + seed → DB reproducible
3. Endpoint list/detail/create (idempotency) → approve/reject (concurrency) → inbox
4. Audit event ditulis dalam transaksi yang sama dengan perubahan state
5. Integration tests untuk business flow, authorization, idempotency, concurrency
6. Frontend: user switcher, create, my requests, inbox, detail + audit timeline, conflict state
7. `REVIEW.md` self-review, `AI_USAGE.md`, `INTEGRITY.md`, README final
8. Verifikasi ulang dari nol (hapus DB, migrate, seed, test, build) → tag `phase-1-complete`

## 6. Test strategy

Integration test lewat HTTP (`WebApplicationFactory`) dengan SQLite file sementara per test — bukan unit test
domain saja, karena yang dinilai adalah perilaku end-to-end termasuk constraint DB.

| Level | Yang dibuktikan |
|---|---|
| Business flow | standard (Approved setelah manager), production & admin (butuh system owner) |
| Authorization | bukan approver → 403, self-approval → 403, admin bukan approver → 403, detail request orang lain → 403/404 |
| Idempotency | ClientRequestId sama (sekuensial & paralel) → satu row, response sama |
| Concurrency | dua approve pada version yang sama → tepat satu 2xx, sisanya 409 |
| State model | terminal state tidak bisa diproses lagi, reject tanpa reason → 400, skip manager → 403 |
| Audit | setiap transition tercatat, append-only (UPDATE/DELETE ditolak) |

Yang sengaja **tidak** dites: UI component test (critical behavior sudah dibuktikan di backend), load test.

## 7. Trade-off penting

**T1. SQLite vs PostgreSQL.** SQLite menang karena reproducibility: satu file, tanpa service, migration tetap
sama bentuknya. Yang dikorbankan: tipe data longgar (string untuk enum) dan write concurrency serialized.
Constraint yang penting (UNIQUE, CHECK, FK, trigger) tetap tersedia dan tetap diuji, jadi invariant yang
dinilai assessment tidak bergantung pada fitur khusus Postgres. Kalau nanti butuh `SELECT ... FOR UPDATE` atau
melindungi dari write skew antar baris, baru pindah ke PostgreSQL (EF Core provider tinggal ganti).

**T2. Concurrency token di aplikasi vs `rowversion`.** SQL Server punya `rowversion` otomatis; SQLite tidak.
Saya pakai kolom `Version` integer + `IsConcurrencyToken()`. Konsekuensinya increment manual (bisa lupa kalau
ada jalur tulis baru), ditutup dengan: satu-satunya jalur tulis state ada di `AccessWorkflow`/endpoint transition
dan dijaga test.

**T3. Idempotency: cek-lalu-insert vs UNIQUE + catch.** Cek-lalu-insert kalah race (dua request lolos cek
sekaligus). Saya pakai UNIQUE index sebagai kebenaran, lalu menangkap pelanggaran constraint dan mengembalikan
row yang sudah ada. Efek samping: butuh jalur "catch → re-read" yang harus tetap mengembalikan payload yang
sama; perbedaan payload dengan key yang sama dijawab 409, bukan diam-diam diterima.

**T4. React SPA (Vite) vs Next.js.** Ini internal tool di belakang API .NET. Next.js menambah runtime server
kedua di depan API, dan server component membuat orang cenderung menaruh logika otorisasi di dua tempat.
SPA statis = satu trust boundary (API), build sederhana, dan CORS/proxy lokal cukup. Yang hilang: SSR/SEO —
tidak relevan untuk aplikasi internal.

## 8. Perubahan plan selama implementasi

Bagian ini sengaja dipertahankan: rencana yang berubah adalah bagian dari evidence, bukan noda.

1. **Concurrency tidak cukup dicek di aplikasi.** Rencana awal: bandingkan `version` di handler.
   Kenyataan: ada celah antara `SELECT` dan `UPDATE`. Ditambahkan `Version` sebagai concurrency token
   (`IsConcurrencyToken()`), sehingga EF mengirim `UPDATE ... WHERE Version = @v`. Perbandingan di
   handler tetap ada, tapi sekarang hanya fast path.
2. **Urutan pengecekan approval berubah.** Awalnya otorisasi approver dicek sebelum versi. Test
   concurrent membuktikan approver yang kalah race menerima 403 "bukan approver", padahal masalahnya
   data basi. Urutan akhir: participant → terminal → versi → approver assignment → reason.
   Konsekuensi yang diterima: user yang bukan participant tetap ditolak lebih dulu (403) agar tidak
   membocorkan state ke orang yang tidak berhak melihat request.
3. **Tambahan yang tidak direncanakan: requester tanpa manajer.** Semua request wajib melewati
   manager approval; user tanpa manajer akan membuat request yang tidak pernah bisa selesai.
   Ditambahkan penolakan 409 `manager_not_configured`. Ikutannya: seed diubah agar semua demo user
   punya manajer (Bob), supaya aturan *requester tidak boleh approve request sendiri* bisa diuji
   end-to-end memakai Carol yang juga System Owner CRM.
4. **Append-only dibuat sebagai constraint database, bukan konvensi.** Rencana awal: tidak ada
   endpoint update/delete untuk audit. Ditambah: trigger SQLite yang menolak `UPDATE`/`DELETE`,
   karena "tidak ada endpoint" tidak melindungi dari bug di kode aplikasi.
5. **Kontrak error dipersempit.** Rencana hanya menyebut "membedakan validation/forbidden/not
   found/conflict". Implementasinya menambahkan `code` mesin-terbaca per kasus
   (`version_conflict`, `idempotency_key_reuse`, `self_approval_forbidden`, …) karena UI perlu
   membedakan UX-nya, bukan hanya status HTTP-nya.
6. **Frontend tanpa router.** Rencana awal menyebut halaman list/detail/inbox. Implementasi memakai
   satu halaman dengan tab + panel detail: jumlah kode lebih kecil, tidak ada state navigasi yang
   harus disinkronkan dengan state server.
7. **Temuan kecil saat verifikasi manual.** Timestamp kehilangan `DateTimeKind.Utc` setelah roundtrip
   SQLite sehingga JSON keluar tanpa `Z` dan jam di UI bergeser. Diperbaiki di mapper
   (`DateTime.SpecifyKind`). Dicatat sebagai R-09 di `REVIEW.md`.
8. **Scope test bertambah.** Rencana 4 kelompok test; hasilnya 31 test karena hampir setiap bug yang
   ditemukan saat smoke test dijadikan test regresi (duplikat paralel, versi basi, self-approval,
   constraint DB langsung).
9. **Delegasi paralel tidak tersedia.** Dokumentasi direncanakan ditulis beberapa subagent sekaligus;
   provider model untuk subagent tidak terkonfigurasi di lingkungan ini, jadi seluruh dokumentasi
   ditulis oleh satu agen. Tidak ada perubahan pada hasil teknis, hanya pada waktu.
10. **Alat verifikasi tambahan.** `scripts/smoke.sh` dibuat agar 7 skenario demo bisa dibuktikan
    dalam satu perintah tanpa membuka browser (dipakai juga sebagai bukti di README).
