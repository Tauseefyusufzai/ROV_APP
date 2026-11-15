

import geopandas as gpd
import contextily as cx
import matplotlib.pyplot as plt
import rasterio
from rasterio.plot import show as rioshow
from shapely.geometry import box
import argparse

#epsg is projection 
def process(epsg):
    parser = argparse.ArgumentParser(
        description="Read shapefile using geopandas.")
    parser.add_argument('input', help='input filename (without .shp)')
    parser.add_argument('-dpi','--pixels', help='input filename (without .shp)')
    parser.add_argument('-o','--output', default=False, required=False, help='output filename (without .png)')

    args = vars(parser.parse_args())
    path = args['input'] + ".shp"
    export_path = args['output']
    dpi = args['pixels']
    fp = (path)
    # Read shapefile as GeoDataFrame
    data = gpd.read_file(fp)
    # print(data.crs)

    # Change CRS of data
    data_new_crs = data.to_crs(epsg=epsg)
    # print(data_new_crs.crs)

    # We can add a line of code to read crs of shapefile and change crs of map tile to match it

    ax = data_new_crs.plot(figsize=(10, 10), alpha=0.2, edgecolor='black')

    # Using an online map as a basemap
    # source = 'https://a.tile.openstreetmap.fr/hot/{z}/{x}/{y}.png'
    # cx.add_basemap(ax, crs=data_new_crs.crs, source=source)

    # Remove axes
    ax.axis("off")

    # Add basemap from .TIF file
    cx.add_basemap(ax, crs=data_new_crs.crs, source="map.tif", zoom=20)

    # Saving the map as a .png file
    plt.savefig(export_path,
                bbox_inches="tight",
                pad_inches=0,
                dpi=dpi,
                transparent=True,
                orientation='landscape')

    # plt.show()


def main():
    process(3857)


if __name__ == "__main__":
    main()
