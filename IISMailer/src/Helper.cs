using System;
using System.Web;
using System.Collections.Specialized;
using System.Text;
using Mono.Csv;
using System.Collections.Generic;
using System.IO;
using IISHelpers;
using IISHelpers.YAML;

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

        //Formats the data using the specified text template
        private string Format(NameValueCollection data, string fldTemplate)
        {
            StringBuilder res = new StringBuilder();
            foreach (string fld in data)
            {
                if (IsValidDataField(fld))
                    res.AppendFormat(fldTemplate, fld, data[fld]);
            }
            return res.ToString();
        }

        //Formats the results to be sent by email
        private string ToEmail(NameValueCollection data)
        {
            return Format(data, "- {0}: {1}\n");
        }
        #endregion
    }
}