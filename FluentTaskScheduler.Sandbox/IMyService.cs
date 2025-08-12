using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentTaskScheduler.Sandbox
{
    public interface IMyService
    {
        Task StepOneAsync();
        Task StepTwoAsync();
    }

}
