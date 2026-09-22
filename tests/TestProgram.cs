using System;
using System.IO;
namespace IconController {
    public static class TestProgram {
        public static int Main(string[] args) {
            if(args.Length!=1) { Console.Error.WriteLine("Usage: IconController.Tests.exe <test-output-directory>");return 2; }
            int result=Tests.Run(args[0]);Console.WriteLine(File.ReadAllText(Path.Combine(args[0],"results.txt")));return result;
        }
    }
}
