# FluentTaskScheduler

A modern, task-based job scheduling library for .NET with a fluent DSL and native dependency injection support.

⏱️ Write readable, chainable, and intuitive task scheduling code.
💉 Fully DI-friendly – perfect for use with `Microsoft.Extensions.DependencyInjection`.
⚙️ Supports interval-based, time-based, and restricted-time-range execution.

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

dotnet add package FluentTaskScheduler --version 0.1.0-beta

---

## ⚙️ Getting Started

### 1. Register the Scheduler

Program.cs:

builder.Services.AddFluentTaskScheduler();
builder.Services.AddSchedulerFor<IMyService>();

builder.Services.AddTransient<IMyService, MyService>();

---

### 2. Create Your Service

public interface IMyService
{
    Task StepOneAsync();
    Task StepTwoAsync();
    Task SyncDailyDataAsync();
    Task GenerateReportAsync();
}

---

### 3. Configure Jobs

var scheduler = host.Services.GetRequiredService<SchedulerBuilder<IMyService>>();

scheduler
    .For(x => x.StepOneAsync())
    .ThenFor(x => x.StepTwoAsync())
    .Every(TimeSpan.FromMinutes(10))
    .Between("09:00", "18:00")
    .Do();

---

## Usage Examples

### Run a job at multiple times every day

scheduler
    .For(x => x.SyncDailyDataAsync())
    .DailyAt("12:00")
    .DailyAt("15:00")
    .DailyAt("19:30")
    .Do();

---

### Run every hour except weekends

scheduler
    .For(x => x.GenerateReportAsync())
    .Every(TimeSpan.FromHours(1))
    .NotRunThisDays(DayOfWeek.Saturday, DayOfWeek.Sunday)
    .Do();

---

### Multi-step workflow

scheduler
    .For(x => x.InitializeAsync())
    .ThenFor(x => x.ProcessDataAsync())
    .ThenFor(x => x.CleanupAsync())
    .Every(TimeSpan.FromMinutes(5))
    .Do();

---

## 🧠 How It Works

- Thread-safe job storage using ConcurrentQueue
- DI-resolved service execution
- Non-blocking background execution using ThreadPool
- AOT-friendly (no reflection invoke)
- Accurate next-run time calculation system

---

## 📁 Folder Structure

FluentTaskScheduler/
 ├─ Core/
 ├─ DSL/
 ├─ Execution/
 ├─ Extensions/

---

## Roadmap

- Cron syntax parser
- Persistent job storage
- Dashboard UI
- Retry policies
- Distributed execution support

---

License: MIT
