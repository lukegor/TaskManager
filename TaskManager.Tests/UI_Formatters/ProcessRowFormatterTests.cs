using TaskManager.Domain.Models;
using TaskManager.UI.Formatters;

namespace TaskManager.Tests.UI_Formatters
{
    public class ProcessRowFormatterTests
    {
        [Fact]
        public void ProcessItem_FormatsAsTabDelimited()
        {
            var process = new Process
            {
                Name = "svc",
                Pid = 42,
                Path = @"C:\tools\svc.exe",
                Priority = 8,
                ThreadCount = 3,
                Ppid = 4
            };

            string formatted = new ProcessRowFormatter().Format(new ProcessItem(process));

            Assert.Equal("svc\t42\tC:\\tools\\svc.exe\t8\t3\t4", formatted);
        }

        [Fact]
        public void Null_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, new ProcessRowFormatter().Format(null));
        }

        [Fact]
        public void NonProcessItem_FallsBackToToString()
        {
            var fallback = new PlainFallback();

            Assert.Equal("fallback-text", new ProcessRowFormatter().Format(fallback));
        }

        private sealed class PlainFallback
        {
            public override string ToString() => "fallback-text";
        }
    }
}
