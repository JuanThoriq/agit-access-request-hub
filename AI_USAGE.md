# AI_USAGE

AI dipakai penuh sesuai aturan assessment: planning, generasi kode, test, refactor, dokumentasi.
Yang dicatat di sini bukan "AI menulis kode", tapi **keputusan mana yang berubah karena review manusia**.

## 1. Idempotency create request

- **Ask**: "Create harus idempotent berdasarkan ClientRequestId, duplikat paralel tidak boleh jadi dua row."
- **Suggestion**: cek `SELECT` dulu berdasarkan `ClientRequestId`; kalau ada, kembalikan yang lama.
- **Accepted / changed**: **changed**. Cek-lalu-insert kalah race: dua request bisa lolos `SELECT`
  bersamaan dan dua-duanya `INSERT`.
- **Why**: jaminan harus ada di tempat yang tidak bisa dirayu oleh timing, yaitu
  `UNIQUE (RequesterId, ClientRequestId)`. Jalur `SELECT` tetap dipertahankan hanya sebagai fast path;
  yang menangani race adalah `catch (DbUpdateException) when IsUniqueConstraintViolation(...)` lalu
  membaca row pemenang.

## 2. Proteksi approval dari aksi bersamaan

- **Ask**: "Dua approver menekan approve pada request yang sama; hanya satu yang boleh berhasil."
- **Suggestion**: kirim `version` di body, bandingkan dengan `version` yang dibaca, lalu `UPDATE`.
- **Accepted / changed**: **changed** (ditambah). Perbandingan di aplikasi saja tidak cukup karena
  masih ada celah antara `SELECT` dan `UPDATE`. Ditambahkan kolom `Version` sebagai
  **concurrency token** (`IsConcurrencyToken()`), sehingga EF menghasilkan
  `UPDATE ... WHERE Id = @id AND Version = @v`; kalau 0 row terpengaruh → `DbUpdateConcurrencyException`
  → 409 `version_conflict`. Cek di aplikasi tetap ada sebagai fast path dengan pesan yang lebih ramah.

## 3. Audit trail

- **Ask**: "Setiap transisi bisnis harus menyimpan audit event secara konsisten/atomic."
- **Suggestion**: tulis audit lewat `ILogger` / file terpisah setelah state berubah.
- **Accepted / changed**: **rejected**. Log bukan audit: bisa hilang, tidak bisa di-query, tidak
  transaksional. Audit menjadi tabel `AuditEvents` yang di-`Add` ke `DbContext` yang sama dengan
  perubahan state → satu `SaveChanges` = satu transaksi. Ditambah trigger SQLite yang menolak
  `UPDATE`/`DELETE` pada tabel itu.

## 4. Bentuk error response

- **Ask**: "Error response harus membedakan validation, forbidden, not found, dan conflict."
- **Suggestion**: semua kegagalan mengembalikan 400 dengan pesan teks.
- **Accepted / changed**: **changed**. Dipakai ProblemDetails (RFC 7807) dengan `code` mesin-terbaca:
  400 `validation_failed` (+ `errors` per field), 401 `missing_identity`/`unknown_demo_user`,
  403 `not_a_participant`/`not_assigned_approver`/`self_approval_forbidden`/`admin_only`,
  404 `request_not_found`, 409 `version_conflict`/`invalid_transition`/`idempotency_key_reuse`/
  `manager_not_configured`. Alasannya: UI harus bisa membedakan "kamu salah isi" vs "kamu tidak boleh"
  vs "datanya sudah berubah" — tiga UX yang berbeda.

## 5. Pilihan frontend

- **Ask**: "Frontend React atau Next.js?"
- **Suggestion**: Next.js (App Router) supaya "fullstack" dan bisa SSR.
- **Accepted / changed**: **rejected**. Ini aplikasi internal di belakang API .NET, identitas datang
  dari header, tidak butuh SEO/SSR. Next.js menambah runtime server kedua di depan API dan memancing
  logika otorisasi tersebar di dua tempat. Dipilih Vite + React + TypeScript (SPA statis, satu
  trust boundary). Konsekuensinya dicatat di `docs/02-ARSITEKTUR.md`.

## Three things AI got wrong

1. **500 pada create yang sukses.** Handler membuat row, lalu memetakan DTO dari entity yang baru
   di-`Add` — navigation `Requester`/`Application` belum terisi, jadi `NullReferenceException`
   setelah data tersimpan. Ditemukan oleh smoke test `curl` (bukan oleh kompilasi).
   Perbaikan: baca ulang entity dengan `Include` sebelum membentuk response.
2. **Recovery duplikat malah 500.** Setelah `DbUpdateException` karena UNIQUE, hanya entity induk
   yang di-detach; `AuditEvent` anaknya masih tracked dengan relasi required →
   "association between entity types has been severed". Ditemukan oleh test paralel
   `ParallelDuplicateCreates_ProduceOneRow`. Perbaikan: `ChangeTracker.Clear()` sebelum re-read.
3. **Field error tidak sampai ke UI.** Error validasi di-flatten ke root ProblemDetails, bukan di
   dalam `errors`, sehingga form tidak bisa menandai field mana yang salah. Ditemukan oleh
   `RejectWithoutReason_IsValidationError`. Perbaikan: nesting `errors` sesuai konvensi.

Dua temuan lain yang juga berasal dari test (bukan dari AI): urutan pengecekan approval memberi 403
ke approver yang kalah race padahal seharusnya 409, dan timestamp kehilangan `DateTimeKind.Utc`
setelah roundtrip SQLite sehingga jam di UI bergeser. Keduanya diperbaiki dan dicatat di `REVIEW.md`.
