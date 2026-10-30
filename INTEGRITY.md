# INTEGRITY

## Declaration

```
I confirm that this submission represents my own engineering work.
AI-assisted tools were used as documented in AI_USAGE.md.
I reviewed the submitted code and can explain its architecture, behavior,
known limitations, security implications, and trade-offs.

Date: 2026-10-31
Primary AI tools used: OptChat coding agent (Claude-based) — planning, code generation,
                       test generation, refactoring, documentation drafting
External human assistance: None
Starter/template code used: `dotnet new web`, `dotnet new xunit` (SDK templates, unmodified),
                            `npm init` style hand-written Vite/React/TypeScript config
External repositories/snippets copied or adapted: None
```

## What "own work" means here, concretely

- Setiap keputusan arsitektur di `PLAN.md` punya alasan tertulis,
  termasuk yang **ditolak** (Next.js, PostgreSQL, OAuth, message broker).
- Semua kode yang di-generate sudah dibaca ulang dan diubah minimal sekali ketika test menemukan
  cacat — daftar jujurnya ada di `AI_USAGE.md` bagian *Three things AI got wrong*.
- Perilaku yang diklaim dibuktikan oleh test yang bisa dijalankan (`dotnet test`), bukan oleh
  pernyataan di dokumen.
- Dokumen requirement asli (PDF assessment, bertanda CONFIDENTIAL) **tidak** dimasukkan ke repository.

## Data dan kredensial

- Tidak ada credential, token, API key, atau data perusahaan nyata di repository.
- Semua user demo adalah alamat fiktif `@example.local` dan password tidak dipakai (identitas
  disimulasikan lewat header `X-Demo-User`).
- Database SQLite dibuat ulang dari migration + seed, tidak pernah di-commit (`.gitignore`).
