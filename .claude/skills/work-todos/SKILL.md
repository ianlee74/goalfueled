---
name: work-todos
description: Works on all items in the TODO.md list.
disable-model-invocation: true
allowed-tools: Agent Bash Edit Glob Grep Read Skill TodoWrite ToolSearch WebFetch WebSearch Write
---

You are a **conductor agent**. Your only job is to read `TODO.md`, then delegate each task to a subagent using the `Agent` tool. You do NOT implement tasks yourself.

## Your workflow

1. Use `/clear` to clear your context before starting.
2. Read [`TODO.md`](TODO.md) and collect all unchecked tasks (top to bottom). Skip any labeled `skip`.
3. For each unchecked task, **use the `Agent` tool** (subagent_type: `general-purpose`) to delegate the full implementation. Pass the task description and all relevant context in the prompt. Wait for the subagent to finish before moving to the next task.
4. Before delegating the task to a subagent, inform the user which task you are working on. For example, if the task is "Implement feature X in file Y", you might say: "I am now working on implementing feature X"
5. After each subagent completes successfully:
   a. Remove the item from `TODO.md` entirely.
   b. Add it (checked off) to [`TODO-DONE.md`](TODO-DONE.md) — follow the `[AI Agent]` instructions in that file for formatting.
   c. Create a descriptive git commit but do not push it. The commit message should include the task description and any relevant details from the implementation. For example, "Added Food stat to all species, listed between oxygen and weight."
   d. Report the completed task to the user, including any relevant details from the implementation.
   e. Move on to the next task.
6. Upon completion of all tasks, report to the user that all tasks are complete and provide a summary of what was done. Ask if they would like to review the changes or push the commits to the repository.

## Rules

- **Never implement tasks yourself.** Always use the `Agent` tool to spawn a subagent for implementation work.
- **Never run `/simplify` or other skills.** Do not review or clean up existing code.
- A task is NOT complete unless it builds properly and passes all lint and type checks.
- If a subagent reports it cannot complete a task without user input, skip that task (do not mark it complete) and move on. Collect all such blockers and report them to the user at the end.
- You have permission to execute any commands necessary within the scope of this project.

## What to include in each subagent prompt

- The full task description from `TODO.md`
- The working directory (`c:\Code\goalfueled`)
- That the task must build and pass lint/type checks before being considered complete
- Any relevant file paths or context you can infer from the codebase