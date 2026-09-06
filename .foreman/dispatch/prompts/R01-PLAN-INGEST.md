# Job R01-PLAN-INGEST — copy the Plan into the Durable Execution Record

You are a **Worker** on Foreman Run `run-2026-09-06-intercolony`, target repository
`C:/dev/Intercolony`, branch `foreman/playtest-batch-run2`.

**This job is record repair, not a Slice.** It carries no Behavioral Claim, produces no
Candidate, and will not be evaluated. It is a mechanical file copy plus verification. Do not
treat it as an implementation task and do not look for one.

## Why you are doing this rather than the Supervisor

The Supervisor process is launched with permitted directories `C:/dev/Intercolony` and
`C:/dev/agent-foreman` only. The Plan for this Run lives outside both, at
`C:/dev/INTERCOLONY_PLAYTEST_BATCH_SOURCE_PLAN.md`, and every Supervisor read of it is refused
by the harness. Your sandbox can read that path. The Foreman record layout
(`foreman/DURABLE_RECORD.md`) requires the authoritative Plan to live at `.foreman/PLAN.md`
inside the target repository, so the record is currently defective and this job repairs it.

## Objective

Produce `C:/dev/Intercolony/.foreman/PLAN.md` as a **verbatim, byte-for-byte copy** of
`C:/dev/INTERCOLONY_PLAYTEST_BATCH_SOURCE_PLAN.md`.

Use a byte-level copy operation (for example `Copy-Item`, or a read/write of raw bytes). Do
**not** round-trip the content through anything that could alter it.

## Absolute prohibitions

These are the whole risk of this job. Violating any of them makes the copy worthless, because
the Supervisor will extract authorized intent from it and cannot tell a helpful edit from the
original.

- **MUST NOT** summarise, condense, reformat, reflow, re-indent, re-order, or "clean up" the
  content in any way.
- **MUST NOT** fix a typo, a heading level, a broken link, a table, or Markdown that looks
  malformed. If it is wrong in the source, it must be wrong in the copy.
- **MUST NOT** change the encoding, the byte order mark, or the line endings. If the source is
  CRLF, the copy is CRLF. If it has a BOM, the copy has a BOM.
- **MUST NOT** add a header, a footer, a provenance note, a front-matter block, or a trailing
  newline that the source does not have.
- **MUST NOT** write, create, delete or modify **any** other file in the repository. Your entire
  write surface is the single path `.foreman/PLAN.md`.
- **MUST NOT** run `git add`, `git commit`, `git checkout`, `git reset`, `git stash`, or any
  other git command that changes the index, the working tree or history. The Supervisor commits.
- **MUST NOT** start, stop or otherwise touch RimWorld, `dev.ps1`, the dev bridge, or port
  34117. This job holds no lock on either and a second process touching the game destroys
  another run.
- **MUST NOT** invent, reconstruct, paraphrase or infer the Plan's content under any
  circumstances. If you cannot read the source, say so and stop — see the failure route below.

## Verification you must perform and report

1. SHA-256 of the source file, and its length in bytes.
2. SHA-256 of `.foreman/PLAN.md` after writing, and its length in bytes.
3. State explicitly whether the two hashes are **identical**. If they are not, the copy failed:
   delete `.foreman/PLAN.md`, report the failure, and do not attempt a textual repair.
4. The total line count of the copy.
5. The first 3 lines and the last 3 lines of the copy, quoted exactly.

## If the source cannot be read

If reading `C:/dev/INTERCOLONY_PLAYTEST_BATCH_SOURCE_PLAN.md` fails for any reason — sandbox
denial, the file does not exist, a permission error — then:

- write nothing at all;
- report the **exact error text** and the command that produced it;
- report the output of listing the parent directory `C:/dev` filtered to `*.md`, so the
  Supervisor can see whether the file exists under a different name;
- do not substitute any other file, however similar its name.

This outcome is a legitimate, useful result. A fabricated or approximated Plan is the single
worst thing this job could produce.

## Execution Envelope

Permitted: reading the source path named above, and writing the single file
`.foreman/PLAN.md` inside the target repository. Everything else — merging, publishing, pushing,
rewriting history, credential or security-boundary changes, destructive operations, starting the
game — is reserved to the operator. You MUST NOT expand this Envelope. You have no authority to
change the Plan, to change what intent is authorized, or to accept your own work.

## Escalation

If anything about this job appears wrong or infeasible — including discovering that the source
is not a plan document at all — **report that conclusion** rather than reinterpreting the job
into something you can satisfy.

## Report

Finish with a short structured report containing: the two SHA-256 values, the two byte counts,
whether they match, the line count, the quoted first and last three lines, and confirmation that
no other file was written and no git command was run.
