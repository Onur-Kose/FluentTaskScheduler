using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentTaskScheduler.Sandbox
{
    public class MyService : IMyService
    {
        private readonly ILogger<MyService> _logger;

        public MyService(ILogger<MyService> logger)
        {
            _logger = logger;
        }

        public Task StepOneAsync()
        {
            _logger.LogInformation("==> Step One çalıştı: {time}", DateTime.Now);
            return Task.CompletedTask;
        }

        public Task StepTwoAsync()
        {
            _logger.LogInformation("==> Step Two çalıştı: {time}", DateTime.Now);
            return Task.CompletedTask;
        }
    }
}
