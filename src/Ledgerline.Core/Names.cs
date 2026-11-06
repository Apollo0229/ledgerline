namespace Ledgerline.Core;

/// <summary>Made-up organisations. Any resemblance to a real company is a coincidence of common words.</summary>
internal static class Names
{
    /// <summary>Treasury clients whose lockboxes receive the payments.</summary>
    public static readonly string[] Clients =
    [
        "Harborview Health System", "Granite Peak Utilities", "Cedar & Pine Property Group", "Meridian Freight Lines",
        "Summit Valley Schools", "Riverbend Water Authority", "Brightline Insurance Mutual", "Oakhurst Medical Partners",
        "Northgate Distribution", "Lakeshore Electric Coop", "Copperfield Manufacturing", "Silverleaf Senior Living",
        "Bluewater Port Authority", "Ironwood Building Supply", "Sagebrush County Treasurer", "Redstone Telecom",
        "Willowmere University", "Fairhaven Dental Group", "Crescent Bay Hospitality", "Pinecrest Waste Services",
        "Highland Agricultural Coop", "Stonebridge Equipment Rental", "Maplewood Pediatrics", "Tidewater Logistics",
        "Evergreen Title & Escrow", "Prairie Gas & Light", "Westbrook Auto Finance", "Juniper Office Products",
        "Clearwater Veterinary Network", "Foxglove Specialty Pharmacy",
    ];

    public static readonly string[] PayerFirst =
    [
        "Acorn", "Alder", "Apex", "Arbor", "Aspen", "Atlas", "Beacon", "Birch", "Blue Ridge", "Boulder", "Bridgeport",
        "Canyon", "Cascade", "Centennial", "Coastal", "Cobalt", "Compass", "Cornerstone", "Cypress", "Delta", "Driftwood",
        "Eastside", "Elm Street", "Emerald", "Falcon", "Fieldstone", "Frontier", "Gateway", "Glacier", "Golden State",
        "Greenfield", "Hawthorne", "Heritage", "Horizon", "Keystone", "Kingsley", "Landmark", "Liberty", "Lighthouse",
        "Magnolia", "Mesa", "Midland", "Mission", "Monarch", "Newport", "Northstar", "Oakridge", "Orchard", "Pacific",
        "Paragon", "Pioneer", "Quarry", "Redwood", "Ridgeline", "Rockport", "Sequoia", "Sierra", "Southpoint",
        "Sterling", "Sycamore", "Thornton", "Trailhead", "Union", "Vanguard", "Vista", "Westfield", "Whitman", "Zenith",
    ];

    public static readonly string[] PayerSecond =
    [
        "Supply", "Medical", "Logistics", "Builders", "Foods", "Dental", "Electric", "Plumbing", "Motors", "Farms",
        "Clinic", "Holdings", "Partners", "Hardware", "Printing", "Analytics", "Roofing", "Brewing", "Textiles",
        "Pharmacy", "Construction", "Landscaping", "Freight", "Marine", "Optical", "Bakery", "Tooling", "Labs",
    ];

    public static readonly string[] PayerSuffix = ["LLC", "Inc.", "Co.", "Group", "Corp.", "& Sons", "Ltd."];
}
