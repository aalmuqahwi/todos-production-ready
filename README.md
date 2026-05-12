# Todos — Production Ready

ASP.NET Core app progressively hardened with production-ready patterns from *Release It!* by Michael Nygard.

## Projects

- **Todos.Web** — ASP.NET Core MVC. Single-user todo app.
- **Todos.Notifications** — ASP.NET Core Web API. Notification service called by `Todos.Web` when a todo is created or has a due date.

## Workflow

Each pattern follows the same steps:

1. **Discuss** — understand the pattern, why it matters, where it belongs
2. **Implement** — apply it to `Todos.Web` and/or `Todos.Notifications`
3. **Document** — concept, gotchas, and code reference in `/docs`
4. **Ship** — commit and push to GitHub