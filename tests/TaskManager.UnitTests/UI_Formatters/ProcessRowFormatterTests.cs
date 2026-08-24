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

            formatted.ShouldBe("svc\t42\tC:\\tools\\svc.exe\t8\t3\t4");
        }

        [Fact]
        public void Null_ReturnsEmptyString()
        {
            new ProcessRowFormatter().Format(null).ShouldBe(string.Empty);
        }

        [Fact]
        public void NonProcessItem_FallsBackToToString()
        {
            var fallback = new PlainFallback();

            new ProcessRowFormatter().Format(fallback).ShouldBe("fallback-text");
        }

        private sealed class PlainFallback
        {
            public override string ToString() => "fallback-text";
        }
    }
}
