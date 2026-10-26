using System.Text.RegularExpressions;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Import;

// Imported steps are plain text, so link each one to a technique from the app's library by the verb it uses
// ("Sear the beef..." -> Searing). Only techniques that exist in the database are ever linked.
public static class TechniqueGuesser
{
    private static readonly (string Technique, string Pattern)[] Verbs =
    [
        ("Stir-Frying", @"\bstir[- ]?fr(y|ies|ied|ying)\b"),
        ("Deep-Frying", @"\bdeep[- ]?fr(y|ies|ied|ying)\b"),
        ("Air Frying", @"\bair[- ]?fr(y|yer|ied|ying)\b"),
        ("Pan-Frying", @"\b(pan[- ]?)?fr(y|ies|ied|ying)\b"),
        ("Searing", @"\bsear(s|ed|ing)?\b"),
        ("Sautéing", @"\bsaut[eé](s|ed|ing)?\b"),
        ("Simmering", @"\bsimmer(s|ed|ing)?\b"),
        ("Braising", @"\bbrais(e|es|ed|ing)\b"),
        ("Stewing", @"\bstew(s|ed|ing)?\b"),
        ("Roasting", @"\broast(s|ed|ing)?\b"),
        ("Baking", @"\bbak(e|es|ed|ing)\b"),
        ("Grilling", @"\bgrill(s|ed|ing)?\b"),
        ("Broiling", @"\bbroil(s|ed|ing)?\b"),
        ("Boiling", @"\b(boil(s|ed|ing)?|bring\b.{0,30}\bto a boil)\b"),
        ("Steaming", @"\bsteam(s|ed|ing)?\b"),
        ("Blanching", @"\bblanch(es|ed|ing)?\b"),
        ("Poaching", @"\bpoach(es|ed|ing)?\b"),
        ("Deglazing", @"\bdeglaz(e|es|ed|ing)\b"),
        ("Reducing", @"\breduc(e|es|ed|ing)\b"),
        ("Caramelizing", @"\bcarameli[sz](e|es|ed|ing)\b"),
        ("Toasting", @"\btoast(s|ed|ing)?\b"),
        ("Marinating", @"\bmarinat(e|es|ed|ing)\b"),
        ("Brining", @"\bbrin(e|es|ed|ing)\b"),
        ("Kneading", @"\bknead(s|ed|ing)?\b"),
        ("Whipping", @"\bwhip(s|ped|ping)?\b"),
        ("Folding", @"\bfold(s|ed|ing)?\s+(in|the|it)\b"),
        ("Creaming", @"\bcream(s|ed|ing)?\s+(together\s+)?the\s+butter\b"),
        ("Basting", @"\bbast(e|es|ed|ing)\b"),
        ("Smoking", @"\bsmok(e|es|ed|ing)\b"),
        ("Proofing", @"\b(proof|rise)(s|ed|ing)?\b.{0,40}\b(dough|until doubled)\b"),
    ];

    public static Technique? Guess(string instruction, IReadOnlyDictionary<string, Technique> byName)
    {
        foreach (var (name, pattern) in Verbs)
            if (byName.TryGetValue(name, out var t) && Regex.IsMatch(instruction, pattern, RegexOptions.IgnoreCase))
                return t;
        return null;
    }
}
