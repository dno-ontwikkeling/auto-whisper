# Wiz Always-On

## Git — Minimize Round-Trips

### You made the changes → skip discovery, ONE call:

```bash
git add <files> && git commit -m "$(cat <<'EOF'
type(scope): description
EOF
)"
```

### You didn't make the changes → ONE call for context:

```bash
echo '=== STATUS ===' && git status && echo '=== DIFF ===' && git diff --staged && git diff && echo '=== LOG ===' && git log --oneline -5
```

Then ONE follow-up: add + commit.

## Git — Authorization

- Push / PR: NEVER without explicit user authorization
- Branch create/switch: NEVER autonomously — user has multiple windows
- Commit to main: NEVER — use feature branch
- Branch naming: `ACME-XXXXX/desc` for Jira stories, `POC-XXXXX/desc` for internal plans
- Amend pushed commits: NEVER without explicit request
- Heredoc in commits: ALWAYS `<<'EOF'` (quoted) to prevent expansion

## Stories

- TaskCreate/TaskUpdate/TaskGet/TaskList/TodoWrite are intercepted by a PreToolUse hook and redirected to stories-cli. You can use them — stories-cli runs under the hood.
- Worklog MUST come before status transition: `worklog <id> <msg>` then `status <id> complete`
- Include story IDs in commits: `feat(scope): desc (#abc1234/ACME-12345)`

## TDD

If you catch yourself writing code before a test, STOP and delete it. No exceptions without Boss permission.

## Debugging

Escalate after 3 failed fix attempts — that signals an architectural problem, not a hypothesis failure.

## Planning

Use `/wiz:plan` instead of native plan mode.

## Scripts

Use `$WIZ_SCRIPTS` for wiz script paths. Never hardcode cache paths.
