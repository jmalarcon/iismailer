using System;
using System.Web;
using System.Web.Configuration;
using System.Collections.Specialized;
using System.Text;


namespace MIISMailer
{
    public static class Helper
    {
        internal const string HONEYPOT_FIELD_NAME = "miis-email-hpt";
        internal const string FINAL_URL_FIELD_NAME = "miis-email-dest";

        //Returns a param from web.config or a default value for it
        //The defaultValue can be skipped and it will be returned an empty string if it's needed
        public static string GetParamValue(string paramName, string defaultvalue = "")
        {
            string v = WebConfigurationManager.AppSettings[paramName];
            return String.IsNullOrEmpty(v) ? defaultvalue : v;
        }

        //Tries to convert any object to the specified type
        public static T DoConvert<T>(object v)
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
        public static string GetFormDataForEmailFromRequest()
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
            formData.AppendFormat("- Usr-Agent: {0}\n", req.UserAgent);

            return formData.ToString();
        }

        //TODO: Gets a string to be sent to a CSV file

        //Gets current user IP (even if it's forwarded by a proxy
        public static string GetIPAddress()
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
        //By default it takes the value at FINAL_URL_FIELD_NAME if it's valid
        //if there isn't one it takes the referrer
        public static string GetDestinationURL()
        {
            HttpRequest req = HttpContext.Current.Request;
            string dest = req.Form[FINAL_URL_FIELD_NAME];
            return string.IsNullOrWhiteSpace(dest) ? req.UrlReferrer.ToString() : dest;
        }
    }
}