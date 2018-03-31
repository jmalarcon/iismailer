using System;
using System.Web;
using System.Collections.Specialized;
using System.Text;
using Mono.Csv;
using System.Collections.Generic;
using System.IO;
using IISHelpers;
using IISHelpers.YAML;
using System.Net;

namespace IISMailer
{
    internal class Helper
    {
        public const string HONEYPOT_FIELD_NAME = "iismailer-hpt";   //Honeypot field name in the form to check for spam
        public const string FINAL_URL_FIELD_NAME = "iismailer-dest-url"; //Destination URL field to redirect to after sending the email

        public const string WEB_CONFIG_PARAM_PREFIX = "IISMailer:"; //THe prefix to use to search for parameters in web.config

        private SimpleYAMLParser _mailerProps;
        private NameValueCollection _data;

        //Constructor
        public Helper(string mailerDefPath)
        {
            string props = IOHelper.ReadTextFromFile(mailerDefPath);
            //Get the Front Matter of the form action definition file
            _mailerProps = new SimpleYAMLParser(props);
            //Clone current request Form data
            CloneRequestData();
            //Add extra info to the managed data
            AddExtraInfoToRequestData();
        }

        /// <summary>
        /// Returns the value, if any, for a specified field name. It takes the value from the FrontMatter first, and if it's not there, tries to read it from the current Web.config.
        /// In web.config it first tries to read them prefixed with "IISMAiler:" to prevent collision with other products, and then without the prefix.
        /// If it's not present neither in the Front Matter nor the Web.config, returns the specified default value.
        /// </summary>
        /// <param name="name">The name of the field to retrieve</param>
        /// <param name="defValue">The default value to return if it's not present</param>
        /// <returns></returns>
        public string GetParamValue(string name, string defValue = "")
        {
            //Retrieve from the front matter...
            string val = _mailerProps[name];
            if (!string.IsNullOrEmpty(val))
                return val;

            //Retrieve from Web.config using the app-specific prefix or without it if it's not present
            return WebHelper.GetParamValue(WEB_CONFIG_PARAM_PREFIX + name, WebHelper.GetParamValue(name, defValue));
        }

        //Gets a string to be sent through email from the Form in the current request
        //(excluding special params suchs a honeypot or finalURL)
        internal string GetFormDataForEmailFromRequest()
        {
            return ToEmail(_data);
        }

        //Gets the URL for the page to be shown after sending the email
        //By default it takes the value of the "dest.url" parameter in web.config or 
        //the value of the FINAL_URL_FIELD_NAME field in the form if it doesn't exist.
        //if there isn't any of both, it takes the referrer
        internal string GetDestinationURL()
        {
            HttpRequest req = HttpContext.Current.Request;
            string dest = GetParamValue("dest.url", req.Form[FINAL_URL_FIELD_NAME]);
            return string.IsNullOrEmpty(dest) ? req.UrlReferrer.ToString() : dest;
        }

        //Appends data line to the CSV file (if there's one)
        internal void AppendToCSVFile()
        {
            bool csvEnabled = TypesHelper.DoConvert<bool>(GetParamValue("CSV.enabled", "false"));
            string csvPath = GetParamValue("CSV.path", "");
            if (!csvEnabled || string.IsNullOrEmpty(csvPath))
                return;

            csvPath = GetAbsolutePath(csvPath);

            using (StreamWriter swCSV = new StreamWriter(csvPath,true))    //Open file to append data
            {
                using (CsvFileWriter csvFW = new CsvFileWriter(swCSV))
                {
                    csvFW.WriteRow(GetDataAsList(_data));
                }
            }
            
        }

        //This method gets the email from the form and checks if sending a template email is enabled to be sent to them, and sends it
        internal void SendResponseToFormSender()
        {
            //Check if send response is enabled...
            bool isSendResponseEnabled = TypesHelper.DoConvert<bool>(GetParamValue("response.enabled", "false"));
            if ( !isSendResponseEnabled)
                return;

            //...if there's a valid template for it...
            string templatePath = GetParamValue("response.template");
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
            string templateContents = IOHelper.ReadTextFromFile(templatePath);

            //Substitute placeholders for fields, if any
            templateContents = ReplacePlaceholders(templateContents);

            //Get the subject
            string subject = GetParamValue("response.subject", "Thanks for getting in touch");
            subject = ReplacePlaceholders(subject);

            //Send the final email
            Mailer mlr = new Mailer(this);
            mlr.SendMail(
                userEmail, 
                subject,
                templateContents, 
                true);
        }

        //Calls a Webhook in a fire&forget fashion (doesn´t wait for the response)
        //Get's the Webhook data from the current params
        internal void CallWebHook()
        {
            string whURL = GetParamValue("webhook.url");    //Webhook URL
            string whFormat = GetParamValue("webhook.format", "json").ToLower();  //THe method to send data to the webhook (JSON by default, can be FORM too)
            Uri whUri;
            bool isValid = Uri.TryCreate(whURL, UriKind.Absolute, out whUri);
            //Check if Webhook URl is valid and is HTTP or HTTPs, in other case, just return
            if ( !(isValid && (whUri.Scheme == "http" || whUri.Scheme == "https")) )
                return;

            //Make the call to the Webhook passing the form's data in the specified format
            if (whFormat == "form")
            {
                PostFormToURL(whURL);
            }
            else
            {
                //Assume JSON in any other case
                PostJsonToURl(whURL);
            }
        }

        #region Internal auxiliary methods

        //Clone the current request from data to an internal collection to be able to manipulate it
        private void CloneRequestData()
        {
            _data = new NameValueCollection(HttpContext.Current.Request.Form);
        }

        //Check if the received field is a data field or not. Valid fields are those which are not used as
        //instructions from the form, such as the Honeypot field or the Final URL field.
        public static bool IsValidDataField(string fld)
        {
            return fld.ToLower() != HONEYPOT_FIELD_NAME && fld.ToLower() != FINAL_URL_FIELD_NAME;
        }

        //Adds extra info to the data received from the form, such as the user IP, the UserAgent or the Referrer
        private void AddExtraInfoToRequestData()
        {
            HttpRequest req = HttpContext.Current.Request;
            _data.Add("IP", WebHelper.GetIPAddress());
            _data.Add("User-Agent", req.UserAgent);
            _data.Add("Referrer", req.UrlReferrer.ToString());
        }

        //Returns the absolute path to a file if it's a relative one 
        private string GetAbsolutePath(string path)
        {
            //Check if its an absolute path on disk (or in a remote folder)
            if (path.IndexOf(":") < 0 && path.IndexOf(@"\\") < 0)
            {
                //If it's a relative path, get the full file path
                path = HttpContext.Current.Server.MapPath(path);
            }
            return path;
        }

        //Substitutes {{fieldname}} placeholders in the template
        private string ReplacePlaceholders(string contents)
        {
            HttpRequest req = HttpContext.Current.Request;

            string[] fields = TemplatingHelper.GetAllPlaceHolderNames(contents);
            foreach(string fldName in fields)
            {
                string fldVal = req.Form[fldName];
                contents = TemplatingHelper.ReplacePlaceHolder(contents, fldName, fldVal);
            }

            return contents;
        }

        //Post current data to URL using the indicated format
        //https://docs.microsoft.com/en-us/dotnet/framework/network-programming/how-to-send-data-using-the-webrequest-class
        //Posts current data in "form" format to an URL using the POST method
        private void PostToURL(string url, string data, string contentType)
        {
            WebRequest wr = WebRequest.Create(url);
            wr.Method = "POST";
            wr.ContentType = contentType;
            byte[] bData = Encoding.UTF8.GetBytes(data);
            wr.ContentLength = bData.Length;
            using (Stream dataStream = wr.GetRequestStream())
            {
                dataStream.Write(bData, 0, bData.Length);
            }
            WebResponse resp = wr.GetResponse();    //We don't use the response at all (fire and forget, kind-of, since it's not async here, but it's async the handler)
        }


        //Posts current data in "form" format to an URL using the POST method
        private void PostFormToURL(string url)
        {
            PostToURL(url, ToFormDataStr(_data), "application/x-www-form-urlencoded");
        }

        //Posts current data in json format to an URL using the POST method
        private void PostJsonToURl(string url)
        {
            PostToURL(url, ToJsonStr(_data), "application/json");
        }

        #endregion

        #region Formatting

        //Returns data as a list of strings to serialize (without names, just the field values)
        private List<string> GetDataAsList(NameValueCollection data)
        {
            List<string> res = new List<string>(data.Count);
            foreach (string fld in data)
            {
                if (IsValidDataField(fld))
                {
                    res.Add(data[fld]);
                }
            }
            return res;
        }

        /// <summary>
        /// Formats the data using the specified text template
        /// </summary>
        /// <param name="data">The name/value collection to serialize</param>
        /// <param name="fldTemplate">The string used to serialize the data with String.Format</param>
        /// <param name="Separator">The separator for each row</param>
        /// <param name="urlEncode">If values should be encoded as URLs</param>
        /// <param name="EscapeQuotes">If double quotes in fields' data should be escaped adding an extra doble quote (for example for JSON)</param>
        /// <returns></returns>
        private string Format(NameValueCollection data, string fldTemplate, string Separator = "", bool urlEncode = false, bool EscapeQuotes = false)
        {
            StringBuilder res = new StringBuilder();
            for(int i = 0; i<data.Count; i++)
            {
                string fld = data.Keys[i];  //Current field name
                if (IsValidDataField(fld))
                {
                    string fldVal = data[fld];
                    if (EscapeQuotes)
                    {
                        fld = fld.Replace("\"", "\"\"");
                        fldVal = fldVal.Replace("\"", "\"\"");
                    }
                    if (urlEncode)
                    {
                        fld = HttpUtility.UrlEncode(fld);
                        fldVal = HttpUtility.UrlEncode(fldVal);
                    }
                    //Append to the final string
                    res.AppendFormat(fldTemplate, fld, fldVal);

                    //Append only if it's not the latest value (and is not an empty separator)
                    if (!String.IsNullOrEmpty(Separator) && i<data.Count-1)
                        res.Append(Separator);
                }
            }
            return res.ToString();
        }

        //Formats the results to be sent by email
        private string ToEmail(NameValueCollection data)
        {
            return Format(data, "- {0}: {1}", "\n");
        }

        //Converts the data in a Form Data String
        private string ToFormDataStr(NameValueCollection data)
        {
            return Format(data, "{0}={1}", "&", true);
        }

        private string ToJsonStr(NameValueCollection data)
        {
            var res = "{\n";
            res += Format(data, "\"{0}\":\"{1}\"", ",\n", EscapeQuotes:true);
            return res + "\n}";
        }
        #endregion
    }
}