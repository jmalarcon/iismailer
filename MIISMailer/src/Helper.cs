using System;
using System.Web;
using System.Web.Configuration;
using System.Collections.Specialized;
using System.Text;
using Mono.Csv;
using System.Collections.Generic;
using System.IO;

namespace MIISMailer
{
    internal static class Helper
    {
        internal const string HONEYPOT_FIELD_NAME = "miis-email-hpt";
        internal const string FINAL_URL_FIELD_NAME = "miis-email-dest";

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

            //Check if its an absolute path on disk (or in a remote folder)
            if (csvPath.IndexOf(":") <0 && csvPath.IndexOf(@"\\") == 0)
            {
                //If it's a relative path, get the full file path
                csvPath = HttpContext.Current.Server.MapPath(csvPath);
            }

            using (StreamWriter swCSV = new StreamWriter(csvPath,true))    //Open file to append data
            {
                using (CsvFileWriter csvFW = new CsvFileWriter(swCSV))
                {
                    csvFW.WriteRow(GetFormDataForCSVFromRequest());
                }
            }
            
        }
    }
}