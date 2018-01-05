using System;
using System.Web;
using System.Web.Configuration;
using System.Collections.Specialized;
using System.Text;
using Mono.Csv;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace IISMailer
{
    internal static class Helper
    {
        internal const string HONEYPOT_FIELD_NAME = "miis-email-hpt";   //Honeypot field name in the form to check for spam
        internal const string FINAL_URL_FIELD_NAME = "miis-email-dest"; //Destination URL field to redirect to after sending the email

        internal static Regex REGEXFIELDS = new Regex(@"\{[0-9a-zA-Z_]+?\}");  //Regular Expression to find fields in templates only letters, numbers and underscores

        //Returns a param from web.config or a default value for it
        //The defaultValue can be skipped and it will be returned an empty string if it's needed
        internal static string GetParamValue(string paramName, string defaultvalue = "")
        {
            string v = WebConfigurationManager.AppSettings[paramName];
            return String.IsNullOrEmpty(v) ? defaultvalue : v.Trim();
        }

        //Tries to convert any object to the specified type
        internal static T DoConvert<T>(object v)
        {
            try
            {
                return (T) Convert.ChangeType(v, typeof(T));
            }
            catch
            {
                return (T)Activator.CreateInstance(typeof(T));
            }
        }

        //Gets a string to be sent through email from the Form in the current request
        //(excluding special params suchs a honeypot or finalURL)
        internal static string GetFormDataForEmailFromRequest()
        {
            HttpRequest req = HttpContext.Current.Request;
            StringBuilder formData = new StringBuilder();
            foreach (string fld in req.Form)
            {
                if (fld.ToLower() != HONEYPOT_FIELD_NAME && fld.ToLower() != FINAL_URL_FIELD_NAME)
                    formData.AppendFormat("- {0}: {1}\n", fld, req.Form[fld]);
            }
            //Add current user IP and user-agent
            formData.AppendFormat("\n- IP: {0}\n", GetIPAddress());
            formData.AppendFormat("- User-Agent: {0}\n", req.UserAgent);

            return formData.ToString();
        }

        //Gets a string to be sent to a CSV file
        internal static List<string> GetFormDataForCSVFromRequest()
        {
            HttpRequest req = HttpContext.Current.Request;
            List<string> formData = new List<string>();
            foreach (string fld in req.Form)
            {
                if (fld.ToLower() != HONEYPOT_FIELD_NAME && fld.ToLower() != FINAL_URL_FIELD_NAME)
                {
                    formData.Add(req.Form[fld]);
                }
            }
            //Add current user IP and user-agent
            formData.Add(GetIPAddress());
            formData.Add(req.UserAgent);

            return formData;
        }

        //Gets current user IP (even if it's forwarded by a proxy
        internal static string GetIPAddress()
        {
            HttpContext ctx = HttpContext.Current;
            string ip = ctx.Request.ServerVariables["HTTP_X_FORWARDED_FOR"];

            if (!string.IsNullOrEmpty(ip))
            {
                string[] addresses = ip.Split(',');
                if (addresses.Length != 0)
                {
                    return addresses[0];
                }
            }

            return ctx.Request.ServerVariables["REMOTE_ADDR"];
        }

        //Gets the URL for the page to be shown after sending the email
        //By default it takes the value of the "mailer.dest" parameter in web.config or 
        //the value of the FINAL_URL_FIELD_NAME field in the form if it doesn't exist.
        //if there isn't any of both, it takes the referrer
        internal static string GetDestinationURL()
        {
            HttpRequest req = HttpContext.Current.Request;
            string dest = GetParamValue("mailer.dest", req.Form[FINAL_URL_FIELD_NAME]);
            return string.IsNullOrEmpty(dest) ? req.UrlReferrer.ToString() : dest;
        }

        //Appends data line to CSV file
        internal static void AppendToCSVFile()
        {
            string csvPath = GetParamValue("mailer.csv.path", "");
            if (string.IsNullOrEmpty(csvPath))
                return;

            csvPath = GetAbsolutePath(csvPath);

            using (StreamWriter swCSV = new StreamWriter(csvPath,true))    //Open file to append data
            {
                using (CsvFileWriter csvFW = new CsvFileWriter(swCSV))
                {
                    csvFW.WriteRow(GetFormDataForCSVFromRequest());
                }
            }
            
        }

        //This method gets the email from the form and checks if sending a template email is enabled to be sent to them, and sends it
        internal static void SendResponseToFormSender()
        {
            //Check if send response is enabled...
            if ( !Helper.DoConvert<bool>(GetParamValue("mailer.response.enabled", "false")) )
                return;

            //...if there's a valid template for it...
            string templatePath = GetParamValue("mailer.response.template");
            if (string.IsNullOrEmpty(templatePath))
                return;
            templatePath = GetAbsolutePath(templatePath);
            if (!File.Exists(templatePath))
                return;

            //...and if there's a field named "email" in the form
            string userEmail = HttpContext.Current.Request.Form["email"];
            if (string.IsNullOrEmpty(userEmail))
                return;

            //Read template from disk
            string templateContents = ReadTextFromFile(templatePath);

            //Substitute placeholders for fields, if any
            templateContents = ReplacePlaceholders(templateContents);

            //Send the final email
            Mailer.SendMail(
                userEmail, 
                GetParamValue("mailer.response.subject", "Thanks for getting in touch"),
                templateContents, 
                true);
        }

        #region Internal auxiliary methods

        //Returns the absolute path to a file if it's a relative one 
        private static string GetAbsolutePath(string path)
        {
            //Check if its an absolute path on disk (or in a remote folder)
            if (path.IndexOf(":") < 0 && path.IndexOf(@"\\") < 0)
            {
                //If it's a relative path, get the full file path
                path = HttpContext.Current.Server.MapPath(path);
            }
            return path;
        }

        //Reads the full contents of a text file from disk
        private static string ReadTextFromFile(string path)
        {
            using (StreamReader srMD = new StreamReader(path))
            {
                return srMD.ReadToEnd(); //Text file contents
            }
        }

        //Substitutes {fieldname} placeholders in the template
        private static string ReplacePlaceholders(string contents)
        {
            HttpRequest req = HttpContext.Current.Request;

            foreach (Match field in REGEXFIELDS.Matches(contents))
            {
                string fldName = field.Value.Substring(1, field.Value.Length - 2).Trim();
                string fldVal = req.Form[fldName];
                if (!string.IsNullOrEmpty(fldVal))
                    contents = contents.Replace(field.Value, fldVal);
            }

            return contents;
        }

        #endregion
    }
}