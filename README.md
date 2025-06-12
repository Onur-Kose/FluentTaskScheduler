# FluentTaskScheduler

**FluentTaskScheduler** is a modern, developer-friendly background job scheduler for .NET.  
It allows you to define scheduled tasks using fluent, readable syntax — no cron expressions, no config files, no nonsense.

---

## 🚀 Features

- ✅ Fluent API with IntelliSense
- ✅ Daily, repeated, and interval-based task scheduling
- ✅ DI/Scoped service support
- ✅ Lightweight and extensible
- ✅ No cron expressions. Ever.

---

## 📦 Installation

(Coming soon on NuGet)

---

## 🧪 Usage

```csharp
new SchedulerBuilder<IMyService>(serviceProvider)
    .For(s => s.DoSomethingAsync())
    .DailyAt(\"09:00\")
    .Do();
