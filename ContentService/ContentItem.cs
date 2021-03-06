// Copyright (c) Microsoft Corporation.  All rights reserved.
//
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Services;
using PackageThis.com.microsoft.msdn.services;

namespace ContentServiceLibrary
{
        
        
        static public class rootContentItem
        {

            static public readonly List<string> libraries = 
                new List<string> ( new string[]{ "MSDN Library", "TechNet Library" });

            static private string[] contentIds = { "ms310241", "Bb126093" };
            static private string[] versions = { "MSDN.10", "TechNet.10" };

            static public int currentLibrary = 0;

            static public string contentId
            {
                get
                {
                    return contentIds[currentLibrary];
                }
            }

            static public string version
            {
                get
                {
                    return versions[currentLibrary];
                }
            }

            static public string name
            {
                get
                {
                    return libraries[currentLibrary];
                }
            }

            
        }


    public class ContentItem
    {
        public string xml;
        public string metadata;
        public string annotations;
        public string toc;
        public string contentId;
        public int numImages;
        public string links;
        public string application;

        public string contentIdentifier;
        string aKeyword;
        string locale;
        string version;
        string collection;

        public List<Image> images = new List<Image>();

        // Because strings returned from the server are used as filenames for pictures and html files, 
        // validate against very restricted set of characters.
        // \w is equivalent to [a-zA-Z0-9_].
        static Regex validateAsFilename = new Regex(@"^[-\w.]+$");


        public ContentItem(string contentIdentifier, string locale, 
            string version, string collection, string application)
        {
            this.contentIdentifier = contentIdentifier;
            this.locale = locale;
            this.version = version;
            this.collection = collection;
            this.application = application;
        }

        public ContentItem(string contentIdentifier)
        {
            this.contentIdentifier = contentIdentifier;
        }

        // Iterator to return filenames in the format required by the xhtml.
        public IEnumerable ImageFilenames
        {
            get
            {
                for (int i = 0; i < images.Count; i++)
                {
                    if (images[i].data == null)
                        continue;

                    yield return GetImageFilename(images[i]);
                }
            }
        }

        public void Load(bool loadImages)
        {
            Load(loadImages, true);
        }

        // Added the loadFailSafe optimization
        public void Load(bool loadImages, bool loadFailSafe)
        {

            getContentRequest request = new getContentRequest();

            request.contentIdentifier = contentIdentifier;
            request.locale = locale;
            request.version = collection + "." + version;

            List<requestedDocument> documents = new List<requestedDocument>();


            requestedDocument document = new requestedDocument();
            document.selector = "Mtps.Links";
            document.type = documentTypes.common;
            documents.Add(document);

            document = new requestedDocument();
            document.type = documentTypes.primary;
            document.selector = "Mtps.Toc";
            documents.Add(document);

            document = new requestedDocument();
            document.type = documentTypes.common;
            document.selector = "Mtps.Search";
            documents.Add(document);

            document = new requestedDocument();
            document.type = documentTypes.feature;
            document.selector = "Mtps.Annotations";
            documents.Add(document);

            if (loadFailSafe == true)
            {
                document = new requestedDocument();
                document.type = documentTypes.primary;
                document.selector = "Mtps.Failsafe";
                documents.Add(document);

            }

            request.requestedDocuments = documents.ToArray();

            ContentService proxy = new ContentService();
            proxy.appIdValue = new appId();
            proxy.appIdValue.value = application;

            getContentResponse response;

            try
            {
                response = proxy.GetContent(request);
            }
            catch
            {
                return;
            }

            if (validateAsFilename.Match(response.contentId).Success == true)
            {
                contentId = response.contentId;
            }
            else
            {
                throw (new BadContentIdException("ContentId contains illegal characters: [" + contentId + "]"));
            }
            
            numImages = response.imageDocuments.Length;
            

            foreach (common commonDoc in response.commonDocuments)
            {
                if (commonDoc.Any != null)
                {
                    switch (commonDoc.commonFormat.ToLower())
                    {
                        case "mtps.search":
                            metadata = commonDoc.Any[0].OuterXml;
                            break;

                        case "mtps.links":
                            links = commonDoc.Any[0].OuterXml;
                            break;

                    }
                }
            }

            foreach (primary primaryDoc in response.primaryDocuments)
            {
                if (primaryDoc.Any != null)
                {
                    switch (primaryDoc.primaryFormat.ToLower())
                    {
                        case "mtps.failsafe":
                            xml = primaryDoc.Any.OuterXml;
                            break;

                        case "mtps.toc":
                            toc = primaryDoc.Any.OuterXml;
                            break;
                    }
                }
            }


            foreach (feature featureDoc in response.featureDocuments)
            {
                if (featureDoc.Any != null)
                {
                    if (featureDoc.featureFormat.ToLower() == "mtps.annotations")
                    {
                        annotations = featureDoc.Any[0].OuterXml;
                    }
                }
            }

            // If we get no meta/search or wiki data, plug in NOP data because
            // we can't LoadXml an empty string nor pass null navigators to
            // the transform.
            if (string.IsNullOrEmpty(metadata) == true)
                metadata = "<se:search xmlns:se=\"urn:mtpg-com:mtps/2004/1/search\" />";
            if (string.IsNullOrEmpty(annotations) == true)
                annotations = "<an:annotations xmlns:an=\"urn:mtpg-com:mtps/2007/1/annotations\" />";


            if (loadImages == true)
            {
                requestedDocument[] imageDocs = new requestedDocument[response.imageDocuments.Length];

                // Now that we know their names, we run a request with each image.
                for (int i = 0; i < response.imageDocuments.Length; i++)
                {
                    imageDocs[i] = new requestedDocument();
                    imageDocs[i].type = documentTypes.image;
                    imageDocs[i].selector = response.imageDocuments[i].name + "." +
                        response.imageDocuments[i].imageFormat;
                }

                request.requestedDocuments = imageDocs;
                response = proxy.GetContent(request);

                foreach (image imageDoc in response.imageDocuments)
                {
                    string imageFilename = imageDoc.name + "." + imageDoc.imageFormat;
                    if (validateAsFilename.Match(imageFilename).Success == true)
                    {
                        images.Add(new Image(imageDoc.name, imageDoc.imageFormat, imageDoc.Value));

                    }
                    else
                    {
                        throw (new BadImageNameExeception(
                            "Image filename contains illegal characters: [" + imageFilename + "]"));
                    }

                }
            }

        }

        // Returns the navigation node that corresponds to this content. If
        // we give it a navigation node already, it'll return that node, so
        // no harm done.
        public string GetNavigationNode()
        {
            // Load the contentItem. If we get a Toc entry, then we know it is
            // a navigation node rather than a content node. The reason is that
            // getNavigationPaths only returns the root node if the target node is
            // a navigation node already. We could check to see if we get one path
            // consisting of one node, but the user could give a target node that is
            // the same as the root node. Perf isn't an issue because this should
            // only be called once with the rootNode.

            this.Load(false); // Don't load images in case we are a content node.
            
            if(toc != null)
                return contentId;

            navigationKey root = new navigationKey();
            root.contentId = rootContentItem.contentId;
            root.locale = locale;
            root.version = rootContentItem.version;

            navigationKey target = new navigationKey();
//            target.contentId = "AssetId:" + assetId;
            target.contentId = contentId;
            target.locale = locale;
            target.version = collection + "." + version;

            ContentService proxy = new ContentService();
            getNavigationPathsRequest request = new getNavigationPathsRequest();
            request.root = root;
            request.target = target;

            getNavigationPathsResponse response = proxy.GetNavigationPaths(request);

            // We need to deal with the case where the content appears in many
            // places in the TOC. For now, just use the first path.
            if (response.navigationPaths.Length == 0)
                return null;

            // This is the last node in the first path.
           return response.navigationPaths[0].navigationPathNodes[
                response.navigationPaths[0].navigationPathNodes.Length - 1].navigationNodeKey.contentId;
           
        }

        public void Write(string directory)
        {
            Write(directory, contentId + ".htm");
        }

        public void Write(string directory, string filename)
        {
            if (xml != null)
            {
                using (StreamWriter sw = new StreamWriter(Path.Combine(directory, filename)))
                {
                    sw.Write(xml);

                }

                foreach (Image image in images)
                {

                    if (image.data == null)
                        continue;

                    string imageFilename = GetImageFilename(image);

                    using (BinaryWriter bw = new BinaryWriter(File.Open(Path.Combine(directory, imageFilename),
                        FileMode.Create)))
                    {

                        bw.Write(image.data, 0, image.data.Length);
                    }

                }
            }

        }

        private string GetImageFilename(Image image)
        {
            return contentId + "." + image.name + "(" + locale +
                 "," + collection + "." + version + ")." + image.imageFormat;

        }
    }

    public class BadContentIdException : ApplicationException
    {
        public BadContentIdException(string message) : base(message)
        {
        }

    }

    public class BadImageNameExeception : ApplicationException
    {
        public BadImageNameExeception(string message) : base(message)
        {
        }
    }
}
