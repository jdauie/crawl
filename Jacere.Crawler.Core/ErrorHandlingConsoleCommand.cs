using System;
using System.Security.Principal;
using ManyConsole;

namespace Jacere.Crawler.Core
{
    public abstract class ErrorHandlingConsoleComand : ConsoleCommand
    {
        private const int Success = 0;
        private const int Failure = 2;

        private bool _requiresAdmin;

        private static bool IsUserAdministrator()
        {
            try
            {
                using (var user = WindowsIdentity.GetCurrent())
                {
                    try
                    {
                        var principal = new WindowsPrincipal(user);
                        return principal.IsInRole(WindowsBuiltInRole.Administrator);
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        protected void IsAdmin()
        {
            _requiresAdmin = true;
        }

        public override int Run(string[] remainingArguments)
        {
            try
            {
                if (_requiresAdmin && !IsUserAdministrator())
                {
                    throw new UnauthorizedAccessException("elevated admin permissions required");
                }

                RunAction(remainingArguments);

                return Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);

                return Failure;
            }
        }

        protected abstract void RunAction(string[] remainingArguments);
    }
}
