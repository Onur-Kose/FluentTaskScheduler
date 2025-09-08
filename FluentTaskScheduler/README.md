# FluentTaskScheduler

A modern, task-based job scheduling library for .NET with a fluent DSL and native dependency injection support.

> ⏱️ Write readable, chainable, and intuitive task scheduling code.
>  
> 💉 Fully DI-friendly – perfect for use with `Microsoft.Extensions.DependencyInjection`.
>  
> ⚙️ Supports interval-based, time-based, and restricted-time-range execution.

---

## ✨ Features

- Fluent API design (`For()`, `ThenFor()`, `Every()`, `DailyAt()`, `Between()`, `NotRunThisDays()` etc.)
- Chainable task steps
- Interval and specific-time support
- Excluded day filtering
- Multiple daily execution times
- Easy testability
- No external dependencies

---

## 📦 Installation

```bash
dotnet add package FluentTaskScheduler --version 0.1.0-beta


'
new SchedulerBuilder<IMyService>(host.Services)
    .For(x => x.StepOneAsync())
    .ThenFor(x => x.StepTwoAsync())
    .Every(TimeSpan.FromMinutes(10))
    .Between("09:00", "18:00")
    .Do();
'
