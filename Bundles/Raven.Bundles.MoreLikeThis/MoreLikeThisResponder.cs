﻿//-----------------------------------------------------------------------
// <copyright file="MoreLikeThisResponder.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Raven.Abstractions.Indexing;
using Raven.Database.Extensions;
using Raven.Database.Server.Abstractions;
using Raven.Database.Server.Responders;
using Constants = Raven.Abstractions.Data.Constants;

namespace Raven.Bundles.MoreLikeThis
{
	public class MoreLikeThisResponder : RequestResponder
	{
        //private readonly Analyzer DEFAULT_ANALYZER = new StandardAnalyzer(Version.LUCENE_29);

        //private readonly Analyzer DEFAULT_ANALYZER = new WhitespaceAnalyzer();

		public override string UrlPattern
		{
			get { return @"^/morelikethis/([\w\-_]+)/(.+)"; } // /morelikethis/(index-name)/(ravendb-document-id)
		}

		public override string[] SupportedVerbs
		{
			get { return new[] { "GET" }; }
		}

		public override void Respond(IHttpContext context)
		{
			var match = urlMatcher.Match(context.GetRequestUrl());
			var indexName = match.Groups[1].Value;
			var documentId = match.Groups[2].Value;

			var fieldNames = context.Request.QueryString.GetValues("fields");

		    var parameters = new MoreLikeThisQueryParameters
		                         {
                                     Boost = GetNullableBool(context.Request.QueryString.Get("boost")),
                                     MaximumNumberOfTokensParsed = GetNullableInt(context.Request.QueryString.Get("maxNumTokens")),
                                     MaximumQueryTerms = GetNullableInt(context.Request.QueryString.Get("maxQueryTerms")),
                                     MaximumWordLength = GetNullableInt(context.Request.QueryString.Get("maxWordLen")),
                                     MinimumDocumentFrequency = GetNullableInt(context.Request.QueryString.Get("minDocFreq")),
                                     MinimumTermFrequency = GetNullableInt(context.Request.QueryString.Get("minTermFreq")),
                                     MinimumWordLength = GetNullableInt(context.Request.QueryString.Get("minWordLen"))
		                         };
            

			var indexDefinition = Database.IndexDefinitionStorage.GetIndexDefinition(indexName);
			if (indexDefinition == null)
			{
				context.SetStatusToNotFound();
				context.WriteJson(new {Error = "The index " + indexName + " cannot be found"});
				return;
			}


			PerformSearch(context, indexName, fieldNames, documentId, indexDefinition, parameters);
		}

	    private void PerformSearch(IHttpContext context, string indexName, string[] fieldNames, string documentId, IndexDefinition indexDefinition, MoreLikeThisQueryParameters parameters)
		{
			IndexSearcher searcher;
			using (Database.IndexStorage.GetCurrentIndexSearcher(indexName, out searcher))
			{
				var td = searcher.Search(new TermQuery(new Term(Constants.DocumentIdFieldName, documentId)), 1);
			    // get the current Lucene docid for the given RavenDB doc ID
				if (td.ScoreDocs.Length == 0)
				{
					context.SetStatusToNotFound();
					context.WriteJson(new {Error = "Document " + documentId + " could not be found"});
					return;
				}
			    var ir = searcher.GetIndexReader();
                var mlt = new RavenMoreLikeThis(ir);

			    AssignParameters(mlt, parameters);

                fieldNames = fieldNames ?? GetFieldNames(ir);
                mlt.SetFieldNames(fieldNames);

			    mlt.Analyzers = GetAnalyzers(indexDefinition, fieldNames);

				var mltQuery = mlt.Like(td.ScoreDocs[0].doc);
				var tsdc = TopScoreDocCollector.create(context.GetPageSize(Database.Configuration.MaxPageSize), true);
				searcher.Search(mltQuery, tsdc);
				var hits = tsdc.TopDocs().ScoreDocs;

				var documentIds = hits.Select(hit => searcher.Doc(hit.doc).Get(Constants.DocumentIdFieldName)).Distinct();

				context.WriteJson(
					from docId in documentIds
					let doc = Database.Get(docId, null)
					where doc != null
					select doc
					);
			}
		}

        private static int? GetNullableInt(string value)
        {
            if (value == null) return null;
            return int.Parse(value);
        }

        private static bool? GetNullableBool(string value)
        {
            if (value == null) return null;
            return true;
        }

	    private void AssignParameters(Similarity.Net.MoreLikeThis mlt, MoreLikeThisQueryParameters parameters)
	    {
	        if(parameters.Boost != null) mlt.SetBoost(parameters.Boost.Value);
            if(parameters.MaximumNumberOfTokensParsed!= null) mlt.SetMaxNumTokensParsed(parameters.MaximumNumberOfTokensParsed.Value);
            if(parameters.MaximumQueryTerms != null) mlt.SetMaxQueryTerms(parameters.MaximumQueryTerms.Value);
            if(parameters.MaximumWordLength != null) mlt.SetMaxWordLen(parameters.MaximumWordLength.Value);
            if(parameters.MinimumDocumentFrequency != null) mlt.SetMinDocFreq(parameters.MinimumDocumentFrequency.Value);
            if(parameters.MinimumTermFrequency != null) mlt.SetMinTermFreq(parameters.MinimumTermFrequency.Value);
            if(parameters.MinimumWordLength != null) mlt.SetMinWordLen(parameters.MinimumWordLength.Value);
	    }

	    private static Dictionary<string, Analyzer> GetAnalyzers(IndexDefinition indexDefinition, IEnumerable<string> fieldNames)
	    {
	        var dictionary = new Dictionary<string, Analyzer>();
	        foreach (var fieldName in fieldNames)
	        {
	            dictionary.Add(fieldName, indexDefinition.GetAnalyzer(fieldName));
	        }
	        return dictionary;
	    }

	    private static string[] GetFieldNames(IndexReader indexReader)
	    {
            var fields = indexReader.GetFieldNames(IndexReader.FieldOption.INDEXED);
	        return fields.Where(x => x != "__document_id").ToArray();
	    }
	}
}
