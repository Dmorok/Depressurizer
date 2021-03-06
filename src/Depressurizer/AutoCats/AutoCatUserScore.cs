﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using System.Xml.Serialization;
using Depressurizer.Core.AutoCats;
using Depressurizer.Core.Enums;
using Depressurizer.Core.Helpers;
using Depressurizer.Core.Models;

namespace Depressurizer.AutoCats
{
    public class AutoCatUserScore : AutoCat
    {
        #region Constants

        public const string TypeIdString = "AutoCatUserScore";

        public const string XmlName_Rule_MaxReviews = "MaxReviews";

        public const string XmlName_Rule_MaxScore = "MaxScore";

        public const string XmlName_Rule_MinReviews = "MinReviews";

        public const string XmlName_Rule_MinScore = "MinScore";

        public const string XmlName_Rule_Text = "Text";

        public const string XmlName_UseWilsonScore = "UseWilsonScore";

        #endregion

        #region Fields

        [XmlElement("Rule")]
        public List<UserScoreRule> Rules;

        #endregion

        #region Constructors and Destructors

        public AutoCatUserScore(string name, string filter = null, string prefix = null, bool useWilsonScore = false, List<UserScoreRule> rules = null, bool selected = false) : base(name)
        {
            Filter = filter;
            Prefix = prefix;
            UseWilsonScore = useWilsonScore;
            Rules = rules ?? new List<UserScoreRule>();
            Selected = selected;
        }

        public AutoCatUserScore(AutoCatUserScore other) : base(other)
        {
            Filter = other.Filter;
            Prefix = other.Prefix;
            UseWilsonScore = other.UseWilsonScore;
            Rules = other.Rules.ConvertAll(rule => new UserScoreRule(rule));
            Selected = other.Selected;
        }

        //XmlSerializer requires a parameterless constructor
        private AutoCatUserScore() { }

        #endregion

        #region Public Properties

        /// <inheritdoc />
        public override AutoCatType AutoCatType => AutoCatType.UserScore;

        public bool UseWilsonScore { get; set; }

        #endregion

        #region Properties

        private static Logger Logger => Logger.Instance;

        #endregion

        #region Public Methods and Operators

        public static AutoCatUserScore LoadFromXmlElement(XmlElement xElement)
        {
            string name = XmlUtil.GetStringFromNode(xElement[Serialization.Constants.Name], TypeIdString);
            string filter = XmlUtil.GetStringFromNode(xElement[Serialization.Constants.Filter], null);
            string prefix = XmlUtil.GetStringFromNode(xElement[Serialization.Constants.Prefix], string.Empty);
            bool useWilsonScore = XmlUtil.GetBoolFromNode(xElement[XmlName_UseWilsonScore], false);

            List<UserScoreRule> rules = new List<UserScoreRule>();
            XmlNodeList nodeList = xElement.SelectNodes(Serialization.Constants.Rule);
            if (nodeList != null)
            {
                foreach (XmlNode node in nodeList)
                {
                    string ruleName = XmlUtil.GetStringFromNode(node[XmlName_Rule_Text], string.Empty);
                    int ruleMin = XmlUtil.GetIntFromNode(node[XmlName_Rule_MinScore], 0);
                    int ruleMax = XmlUtil.GetIntFromNode(node[XmlName_Rule_MaxScore], 100);
                    int ruleMinRev = XmlUtil.GetIntFromNode(node[XmlName_Rule_MinReviews], 0);
                    int ruleMaxRev = XmlUtil.GetIntFromNode(node[XmlName_Rule_MaxReviews], 0);
                    rules.Add(new UserScoreRule(ruleName, ruleMin, ruleMax, ruleMinRev, ruleMaxRev));
                }
            }

            AutoCatUserScore result = new AutoCatUserScore(name, filter, prefix, useWilsonScore)
            {
                Rules = rules
            };

            return result;
        }

        /// <inheritdoc />
        public override AutoCatResult CategorizeGame(GameInfo game, Filter filter)
        {
            if (games == null)
            {
                Logger.Error(GlobalStrings.Log_AutoCat_GamelistNull);
                throw new ApplicationException(GlobalStrings.AutoCatGenre_Exception_NoGameList);
            }

            if (game == null)
            {
                Logger.Error(GlobalStrings.Log_AutoCat_GameNull);
                return AutoCatResult.Failure;
            }

            if (!Database.Contains(game.Id, out DatabaseEntry entry))
            {
                return AutoCatResult.NotInDatabase;
            }

            if (!game.IncludeGame(filter))
            {
                return AutoCatResult.Filtered;
            }

            int score = entry.ReviewPositivePercentage;
            int reviews = entry.ReviewTotal;
            if (UseWilsonScore && reviews > 0)
            {
                // calculate the lower bound of the Wilson interval for 95 % confidence
                // see http://www.evanmiller.org/how-not-to-sort-by-average-rating.html
                // $$ w^\pm = \frac{1}{1+\frac{z^2}{n}}
                // \left( \hat p + \frac{z^2}{2n} \pm z \sqrt{ \frac{\hat p (1 - \hat p)}{n} + \frac{z^2}{4n^2} } \right)$$
                // where
                // $\hat p$ is the observed fraction of positive ratings (proportion of successes),
                // $n$ is the total number of ratings (the sample size), and
                // $z$ is the $1-{\frac {\alpha}{2}}$ quantile of a standard normal distribution
                // for 95% confidence, the $z = 1.96$
                double z = 1.96; // normal distribution of (1-(1-confidence)/2), i.e. normal distribution of 0.975 for 95% confidence
                double p = score / 100.0;
                double n = reviews;
                p = Math.Round(100 * ((p + z * z / (2 * n) - z * Math.Sqrt((p * (1 - p) + z * z / (4 * n)) / n)) / (1 + z * z / n)));
                // debug: System.Windows.Forms.MessageBox.Show("score " + score + " of " + reviews + " is\tp = " + p + "\n");
                score = Convert.ToInt32(p);
            }

            string result = null;
            foreach (UserScoreRule rule in Rules)
            {
                if (!CheckRule(rule, score, reviews))
                {
                    continue;
                }

                result = rule.Name;
                break;
            }

            if (result == null)
            {
                return AutoCatResult.Success;
            }

            result = GetCategoryName(result);
            game.AddCategory(games.GetCategory(result));

            return AutoCatResult.Success;
        }

        /// <inheritdoc />
        public override AutoCat Clone()
        {
            return new AutoCatUserScore(this);
        }

        /// <summary>
        ///     Generates rules that match the Steam Store rating labels
        /// </summary>
        /// <param name="rules">List of UserScoreRule objects to populate with the new ones. Should generally be empty.</param>
        public void GenerateSteamRules(ICollection<UserScoreRule> rules)
        {
            rules.Add(new UserScoreRule(GlobalStrings.AutoCatUserScore_Preset_Steam_Positive4, 95, 100, 500, 0));
            rules.Add(new UserScoreRule(GlobalStrings.AutoCatUserScore_Preset_Steam_Positive3, 85, 100, 50, 0));
            rules.Add(new UserScoreRule(GlobalStrings.AutoCatUserScore_Preset_Steam_Positive2, 80, 100, 1, 0));
            rules.Add(new UserScoreRule(GlobalStrings.AutoCatUserScore_Preset_Steam_Positive1, 70, 79, 1, 0));
            rules.Add(new UserScoreRule(GlobalStrings.AutoCatUserScore_Preset_Steam_Mixed, 40, 69, 1, 0));
            rules.Add(new UserScoreRule(GlobalStrings.AutoCatUserScore_Preset_Steam_Negative1, 20, 39, 1, 0));
            rules.Add(new UserScoreRule(GlobalStrings.AutoCatUserScore_Preset_Steam_Negative4, 0, 19, 500, 0));
            rules.Add(new UserScoreRule(GlobalStrings.AutoCatUserScore_Preset_Steam_Negative3, 0, 19, 50, 0));
            rules.Add(new UserScoreRule(GlobalStrings.AutoCatUserScore_Preset_Steam_Negative2, 0, 19, 1, 0));
        }

        /// <inheritdoc />
        public override void WriteToXml(XmlWriter writer)
        {
            writer.WriteStartElement(TypeIdString);

            writer.WriteElementString(Serialization.Constants.Name, Name);
            if (Filter != null)
            {
                writer.WriteElementString(Serialization.Constants.Filter, Filter);
            }

            if (Prefix != null)
            {
                writer.WriteElementString(Serialization.Constants.Prefix, Prefix);
            }

            writer.WriteElementString(XmlName_UseWilsonScore, UseWilsonScore.ToString().ToLowerInvariant());

            foreach (UserScoreRule rule in Rules)
            {
                writer.WriteStartElement(Serialization.Constants.Rule);
                writer.WriteElementString(XmlName_Rule_Text, rule.Name);
                writer.WriteElementString(XmlName_Rule_MinScore, rule.MinScore.ToString(CultureInfo.InvariantCulture));
                writer.WriteElementString(XmlName_Rule_MaxScore, rule.MaxScore.ToString(CultureInfo.InvariantCulture));
                writer.WriteElementString(XmlName_Rule_MinReviews, rule.MinReviews.ToString(CultureInfo.InvariantCulture));
                writer.WriteElementString(XmlName_Rule_MaxReviews, rule.MaxReviews.ToString(CultureInfo.InvariantCulture));

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        #endregion

        #region Methods

        private static bool CheckRule(UserScoreRule rule, int score, int reviews)
        {
            return score >= rule.MinScore && score <= rule.MaxScore && rule.MinReviews <= reviews && (rule.MaxReviews == 0 || rule.MaxReviews >= reviews);
        }

        #endregion
    }
}
