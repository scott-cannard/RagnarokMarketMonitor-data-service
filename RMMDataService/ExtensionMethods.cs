using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RMMDataService
{
    public static class ExtensionMethods
    {
        /// <summary>
        /// HttpClient extension: .GETResponseHtmlString
        /// </summary>
        /// <param name="request">GET-request as string</param>
        /// <returns>HttpResonseMessage content as string</returns>
        async public static Task<string> GETResponseHtmlString(this HttpClient THIS, string request)
        {
            string pageSource = String.Empty;
            try
            {
                HttpResponseMessage response = await THIS.GetAsync(request);
                for (; (pageSource = await response.Content.ReadAsStringAsync()) == null;) ;
            }
            catch
            { }
            return pageSource;
        }

        /// <summary>
        /// String extension: .Between
        /// </summary>
        /// <param name="before">Token string located before the desired substring</param>
        /// <param name="after">Toekn string located after the desired substring</param>
        /// <returns>Substring located between the indicated 'before' and 'after' tokens</returns>
        public static String Between(this String THIS, string before, string after)
        {
            int startIndex = THIS.IndexOf(before) + before.Length;
            int length = THIS.Substring(startIndex).IndexOf(after);
            return (length > 0 ? THIS.Substring(startIndex, length) : THIS.Substring(startIndex));
        }

        /// <summary>
        /// String extension: .RegexGrab
        /// </summary>
        /// <param name="before">Token string located before the desired substring</param>
        /// <param name="pattern">Regex format string to be matched</param>
        /// <param name="after">Token string located after the desired substring</param>
        /// <returns>Substring matching the indicated pattern</returns>
        public static String RegexGrab(this String THIS, string before, string pattern, string after = "")
        {
            return new Regex(String.Format("{0}(?<token>{1}){2}", before, pattern, after)).Match(THIS).Groups["token"].Value;
        }

        /// <summary>
        /// String overload: .Replace
        /// </summary>
        /// <param name="targets">Array of token strings to be replaced</param>
        /// <param name="newPattern">String which will replace target tokens</param>
        /// <returns>Copy of source string after tokens have been replaced</returns>
        public static String Replace(this String THIS, string[] targets, string newPattern)
        {
            StringBuilder result = new StringBuilder(THIS);
            foreach (string oldPattern in targets)
            {
                result = result.Replace(oldPattern, newPattern);
            }
            return result.ToString();
        }

        /// <summary>
        /// String extension
        /// </summary>
        /// <param name="THIS"></param>
        /// <returns></returns>
        public static String ConvertHtmlSymbols(this String THIS)
        {
            return THIS.Replace("&amp;", "&")
                       .Replace("&gt;", ">")
                       .Replace("&lt;", "<");
        }
    }
}
