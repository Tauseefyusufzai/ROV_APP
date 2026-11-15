var express = require("express");
var router = express.Router();

/* POST - Store person path position (accumulate positions for each person) */
router.post("/", function (req, res, next) {
  const PersonPathPosition = req.app.models.PersonPathPosition;
  const Recordings = req.app.models.Recordings;
  
  const {
    serial_no,
    latitude,
    longitude,
    map_id,
    exercise_id,
    recording_id,
    unity_time
  } = req.body;

  // Validate required fields
  if (!serial_no || latitude === undefined || longitude === undefined) {
    return res.status(400).json({
      error: true,
      message: "Missing required fields: serial_no, latitude, longitude"
    });
  }

  const currentTimestamp = new Date().toISOString();
  const lat_value = parseFloat(latitude);
  const lng_value = parseFloat(longitude);
  const unity_time_value = unity_time || null;

  // Resolve exercise_id from the recording's saved exercise_id first, then from the request, then fall back to the latest exercise.
  let exerciseLookupPromise;
  if (recording_id) {
    exerciseLookupPromise = Recordings.findByPk(recording_id, { attributes: ['id', 'exercise_id'] })
      .then((recording) => {
        if (!recording?.exercise_id) {
          return null;
        }

        return req.app.models.Exercise.findByPk(recording.exercise_id, { attributes: ['id', 'map_id'] });
      });
  } else if (exercise_id) {
    exerciseLookupPromise = req.app.models.Exercise.findByPk(exercise_id, { attributes: ['id', 'map_id'] });
  } else {
    exerciseLookupPromise = req.app.models.Exercise.findOne({
      order: [['createdAt', 'DESC']],
      attributes: ['id', 'map_id']
    });
  }

  exerciseLookupPromise
  .then((currentExercise) => {
    const resolvedExerciseId = currentExercise ? currentExercise.id : (exercise_id || null);
    
    // If map_id not provided, get it from the current exercise's map_id
    let resolvedMapId = map_id;
    if (!resolvedMapId && currentExercise?.map_id) {
      resolvedMapId = currentExercise.map_id;
      console.log(`[PersonPathPosition] Resolved map_id=${resolvedMapId} from exercise ${resolvedExerciseId}`);
    }

  // First, find existing record for this person in this specific exercise
  PersonPathPosition.findOne({
    where: { 
      serial_no: serial_no,
      exercise_id: resolvedExerciseId
    }
  })
  .then(existingRecord => {
    if (existingRecord) {
      // Existing record found - append new position to arrays
      const currentLatArray = existingRecord.latitude_array ? existingRecord.latitude_array.split(',') : [];
      const currentLngArray = existingRecord.longitude_array ? existingRecord.longitude_array.split(',') : [];
      const currentTimestampArray = existingRecord.timestamp_array ? existingRecord.timestamp_array.split(',') : [];
      const currentUnityTimeArray = existingRecord.unity_time_array ? existingRecord.unity_time_array.split(',') : [];

      // Append new values
      currentLatArray.push(lat_value.toString());
      currentLngArray.push(lng_value.toString());
      currentTimestampArray.push(currentTimestamp);
      if (unity_time_value) {
        currentUnityTimeArray.push(unity_time_value.toString());
      }

      // Update the record with accumulated arrays
      return existingRecord.update({
        latitude: lat_value, // Keep latest single values for backward compatibility
        longitude: lng_value,
        timestamp: new Date(currentTimestamp),
        map_id: map_id || resolvedMapId || existingRecord.map_id,
        exercise_id: resolvedExerciseId || existingRecord.exercise_id,
        unity_time: unity_time_value || existingRecord.unity_time,
        latitude_array: currentLatArray.join(','),
        longitude_array: currentLngArray.join(','),
        timestamp_array: currentTimestampArray.join(','),
        unity_time_array: currentUnityTimeArray.join(','),
        total_positions: currentLatArray.length
      });
    } else {
      // No existing record - create new one with first position
      return PersonPathPosition.create({
        serial_no: serial_no,
        latitude: lat_value,
        longitude: lng_value,
        timestamp: new Date(currentTimestamp),
        map_id: map_id || resolvedMapId,
        exercise_id: exercise_id || resolvedExerciseId,
        unity_time: unity_time_value,
        latitude_array: lat_value.toString(),
        longitude_array: lng_value.toString(),
        timestamp_array: currentTimestamp,
        unity_time_array: unity_time_value ? unity_time_value.toString() : '',
        total_positions: 1
      });
    }
  })
  .then((result) => {
    const action = result.isNewRecord !== false ? "updated" : "created";
    
    res.json({
      error: false,
      message: `Path position ${action} successfully`,
      data: {
        serial_no: result.serial_no,
        latest_position: {
          latitude: result.latitude,
          longitude: result.longitude,
          timestamp: result.timestamp
        },
        total_positions: result.total_positions,
        position_arrays: {
          latitudes: result.latitude_array,
          longitudes: result.longitude_array,
          timestamps: result.timestamp_array
        }
      },
      action: action
    });
  })
  .catch((error) => {
    console.error("Error storing path position:", error);
    res.status(500).json({
      error: true,
      message: "Failed to store path position",
      details: error.message
    });
  });
  });
});

/* POST - Bulk store multiple path positions (accumulate positions for each person) */
router.post("/bulk", function (req, res, next) {
  const PersonPathPosition = req.app.models.PersonPathPosition;
  let { positions } = req.body;

  // Handle the case where Unity sends positions in an "items" wrapper
  if (positions && positions.items && Array.isArray(positions.items)) {
    positions = positions.items;
  }

  if (!positions || !Array.isArray(positions) || positions.length === 0) {
    return res.status(400).json({
      error: true,
      message: "Missing or invalid positions array"
    });
  }

  // Group positions by serial_no to handle multiple positions per person efficiently
  const positionsByPerson = {};
  positions.forEach(pos => {
    if (!pos.serial_no || pos.latitude === undefined || pos.longitude === undefined) {
      throw new Error("Missing required fields in position data: serial_no, latitude, longitude");
    }
    
    if (!positionsByPerson[pos.serial_no]) {
      positionsByPerson[pos.serial_no] = [];
    }
    positionsByPerson[pos.serial_no].push(pos);
  });

  // Process each person's positions
  const updatePromises = Object.keys(positionsByPerson).map(serial_no => {
    const personPositions = positionsByPerson[serial_no];
    
    // Use exercise_id from first position or global exercise
    const exercise_id = personPositions[0].exercise_id || globalThis.exerciseId;
    
    return PersonPathPosition.findOne({
      where: { 
        serial_no: serial_no,
        exercise_id: exercise_id
      }
    })
    .then(existingRecord => {
      let currentLatArray = [];
      let currentLngArray = [];
      let currentTimestampArray = [];
      let currentUnityTimeArray = [];
      
      // Start with existing arrays if record exists
      if (existingRecord) {
        currentLatArray = existingRecord.latitude_array ? existingRecord.latitude_array.split(',') : [];
        currentLngArray = existingRecord.longitude_array ? existingRecord.longitude_array.split(',') : [];
        currentTimestampArray = existingRecord.timestamp_array ? existingRecord.timestamp_array.split(',') : [];
        currentUnityTimeArray = existingRecord.unity_time_array ? existingRecord.unity_time_array.split(',') : [];
      }
      
      // Add all new positions for this person
      let latestPosition = null;
      personPositions.forEach(pos => {
        const lat_value = parseFloat(pos.latitude);
        const lng_value = parseFloat(pos.longitude);
        const timestamp = pos.timestamp ? new Date(pos.timestamp).toISOString() : new Date().toISOString();
        
        currentLatArray.push(lat_value.toString());
        currentLngArray.push(lng_value.toString());
        currentTimestampArray.push(timestamp);
        
        if (pos.unity_time) {
          currentUnityTimeArray.push(pos.unity_time.toString());
        }
        
        // Keep track of the latest position
        latestPosition = {
          latitude: lat_value,
          longitude: lng_value,
          timestamp: new Date(timestamp),
          map_id: pos.map_id || null,
          exercise_id: pos.exercise_id || exercise_id || null,
          unity_time: pos.unity_time || null
        };
      });
      
      const updateData = {
        latitude: latestPosition.latitude,
        longitude: latestPosition.longitude,
        timestamp: latestPosition.timestamp,
        map_id: latestPosition.map_id,
        exercise_id: latestPosition.exercise_id,
        unity_time: latestPosition.unity_time,
        latitude_array: currentLatArray.join(','),
        longitude_array: currentLngArray.join(','),
        timestamp_array: currentTimestampArray.join(','),
        unity_time_array: currentUnityTimeArray.join(','),
        total_positions: currentLatArray.length
      };
      
      if (existingRecord) {
        return existingRecord.update(updateData).then(() => ({ action: 'updated', serial_no, total_positions: currentLatArray.length }));
      } else {
        updateData.serial_no = serial_no;
        return PersonPathPosition.create(updateData).then(() => ({ action: 'created', serial_no, total_positions: currentLatArray.length }));
      }
    });
  });

  // Execute all updates
  Promise.all(updatePromises)
    .then((results) => {
      let created = 0;
      let updated = 0;
      
      results.forEach(result => {
        if (result.action === 'created') created++;
        else if (result.action === 'updated') updated++;
      });
      
      res.json({
        error: false,
        message: `Bulk accumulation completed: ${created} created, ${updated} updated`,
        summary: {
          total_positions_processed: positions.length,
          people_affected: results.length,
          created: created,
          updated: updated
        },
        details: results
      });
    })
    .catch((error) => {
      console.error("Error bulk accumulating path positions:", error);
      res.status(500).json({
        error: true,
        message: "Failed to accumulate path positions",
        details: error.message
      });
    });
});

/* GET - Get all persons with position data (simplified version) */
router.get("/persons", async function (req, res, next) {
  const PersonPathPosition = req.app.models.PersonPathPosition;
  const Devices = req.app.models.Devices;

  try {
    const results = await PersonPathPosition.findAll({
      attributes: [
        'serial_no',
        'total_positions',
        [PersonPathPosition.sequelize.fn('MAX', PersonPathPosition.sequelize.col('PersonPathPosition.updatedAt')), 'last_update']
      ],
      group: ['serial_no'],
      raw: true
    });

    // Fetch device info (including category) for all persons
    const serialNumbers = results.map(r => r.serial_no);
    const devices = await Devices.findAll({
      where: {
        serial_no: serialNumbers
      },
      attributes: ['serial_no', 'name', 'category'],
      raw: true
    });

    // Create a lookup map for device info
    const deviceMap = {};
    devices.forEach(device => {
      deviceMap[device.serial_no] = device;
    });

    // Transform results to simple format with category
    const persons = results.map(result => {
      const deviceInfo = deviceMap[result.serial_no] || {};
      return {
        serial_no: result.serial_no,
        name: deviceInfo.name || "Person " + result.serial_no,
        category: deviceInfo.category || "", // Include category/team from Devices table
        position_count: parseInt(result.total_positions) || 0,
        last_update: result.last_update
      };
    });

    res.json({
      persons: persons,
      total_count: persons.length,
      status: "success"
    });
  } catch (error) {
    console.error("Error retrieving all persons:", error);
    res.status(500).json({
      error: true,
      message: "Failed to retrieve all persons",
      details: error.message
    });
  }
});

/* GET - Get list of persons with position data for a specific exercise */
router.get("/persons/:exerciseId", async function (req, res, next) {
  const PersonPathPosition = req.app.models.PersonPathPosition;
  const Devices = req.app.models.Devices;
  const { exerciseId } = req.params;

  try {
    // Get all persons with position data for this exercise
    const results = await PersonPathPosition.findAll({
      where: {
        exercise_id: exerciseId
      },
      attributes: [
        'serial_no',
        'total_positions',
        [PersonPathPosition.sequelize.fn('MAX', PersonPathPosition.sequelize.col('PersonPathPosition.updatedAt')), 'last_update']
      ],
      group: ['serial_no'],
      raw: true
    });

    // Fetch device info (including category) for all persons
    const serialNumbers = results.map(r => r.serial_no);
    const devices = await Devices.findAll({
      where: {
        serial_no: serialNumbers
      },
      attributes: ['serial_no', 'name', 'category'],
      raw: true
    });

    // Create a lookup map for device info
    const deviceMap = {};
    devices.forEach(device => {
      deviceMap[device.serial_no] = device;
    });

    // Transform results to match Unity data model with category
    const persons = results.map(result => {
      const deviceInfo = deviceMap[result.serial_no] || {};
      return {
        serial_no: result.serial_no,
        name: deviceInfo.name || result.serial_no,
        category: deviceInfo.category || "", // Include category/team from Devices table
        position_count: parseInt(result.total_positions) || 0,
        last_update: result.last_update
      };
    });

    res.json({
      persons: persons,
      total_count: persons.length,
      status: "success"
    });
  } catch (error) {
    console.error("Error retrieving persons for exercise:", error);
    res.status(500).json({
      error: true,
      message: "Failed to retrieve persons for exercise",
      details: error.message
    });
  }
});

/* GET - Get position arrays for a specific person (with map data) */
/* GET - Get position arrays for a specific person (with optional time-range filtering for video recordings) */
router.get("/arrays/:serial_no", function (req, res, next) {
  const PersonPathPosition = req.app.models.PersonPathPosition;
  const { serial_no } = req.params;
  const { start_time, end_time, exercise_id } = req.query; // NEW: Optional time filtering

  // Build where clause for query
  let whereClause = { serial_no: serial_no };
  if (exercise_id) {
    whereClause.exercise_id = exercise_id;
  }

  PersonPathPosition.findOne({
    where: whereClause,
    order: [['updatedAt', 'DESC']], // Get latest record
    attributes: [
      'serial_no',
      'latitude_array',
      'longitude_array',
      'timestamp_array',
      'total_positions',
      'map_id',
      'exercise_id'
    ],
    include: [
      {
        model: req.app.models.Maps,
        as: 'map',
        attributes: ['id', 'name', 'img_path']
      }
    ]
  })
  .then((result) => {
    if (!result) {
      return res.status(404).json({
        error: true,
        message: `No position data found for person ${serial_no}`
      });
    }

    // Parse arrays
    const allLatitudes = result.latitude_array ? result.latitude_array.split(',').map(Number) : [];
    const allLongitudes = result.longitude_array ? result.longitude_array.split(',').map(Number) : [];
    const allTimestamps = result.timestamp_array ? result.timestamp_array.split(',') : [];

    // NEW: Apply time-range filtering if start_time and end_time are provided
    let filteredLatitudes = allLatitudes;
    let filteredLongitudes = allLongitudes;
    let filteredTimestamps = allTimestamps;
    let filteredCount = result.total_positions;
    let isFiltered = false;

    if (start_time && end_time) {
      try {
        const startDate = new Date(start_time);
        const endDate = new Date(end_time);
        
        // Combine arrays into objects for filtering
        const allPositions = allTimestamps.map((ts, index) => ({
          latitude: allLatitudes[index],
          longitude: allLongitudes[index],
          timestamp: ts
        }));
        
        // Filter positions within time range
        const filteredPositions = allPositions.filter(point => {
          const pointDate = new Date(point.timestamp);
          return pointDate >= startDate && pointDate <= endDate;
        });
        
        // Extract filtered arrays
        filteredLatitudes = filteredPositions.map(p => p.latitude);
        filteredLongitudes = filteredPositions.map(p => p.longitude);
        filteredTimestamps = filteredPositions.map(p => p.timestamp);
        filteredCount = filteredPositions.length;
        isFiltered = true;
      } catch (error) {
        // If filtering fails, fall back to returning all positions
      }
    }

    // Prepare map data
    const mapData = result.map ? {
      id: result.map.id,
      name: result.map.name,
      img_path: result.map.img_path
    } : null;

    // Return response (filtered or full, depending on parameters)
    const response = {
      serial_no: result.serial_no,
      total_positions: filteredCount,
      positions: {
        latitudes: filteredLatitudes,
        longitudes: filteredLongitudes,
        timestamps: filteredTimestamps
      },
      map_id: result.map_id,
      map: mapData,
      exercise_id: result.exercise_id
    };

    // Add filtering metadata if time-range was applied
    if (isFiltered) {
      response.filtered_by_time = true;
      response.time_range = {
        start_time: start_time,
        end_time: end_time
      };
      response.original_total_positions = result.total_positions;
    }

    res.json(response);
  })
  .catch((error) => {
    console.error("Error retrieving position arrays:", error);
    res.status(500).json({
      error: true,
      message: "Failed to retrieve position arrays",
      details: error.message
    });
  });
});

/* GET - Retrieve path positions for a specific person */
router.get("/:serial_no", function (req, res, next) {
  const PersonPathPosition = req.app.models.PersonPathPosition;
  const { serial_no } = req.params;
  const { exercise_id, limit, offset } = req.query;

  let whereCondition = { serial_no: serial_no };
  
  if (exercise_id) {
    whereCondition.exercise_id = exercise_id;
  } else if (globalThis.exerciseId) {
    whereCondition.exercise_id = globalThis.exerciseId;
  }

  const queryOptions = {
    where: whereCondition,
    order: [['timestamp', 'ASC']],
    include: [
      {
        model: req.app.models.Devices,
        as: 'device',
        attributes: ['name', 'type']
      },
      {
        model: req.app.models.Maps,
        as: 'map',
        attributes: ['name']
      }
    ]
  };

  if (limit) {
    queryOptions.limit = parseInt(limit);
  }
  if (offset) {
    queryOptions.offset = parseInt(offset);
  }

  PersonPathPosition.findAll(queryOptions)
    .then((pathPositions) => {
      res.json({
        error: false,
        data: pathPositions,
        total: pathPositions.length
      });
    })
    .catch((error) => {
      console.error("Error retrieving path positions:", error);
      res.status(500).json({
        error: true,
        message: "Failed to retrieve path positions",
        details: error.message
      });
    });
});

/* REMOVED: Duplicate /arrays route - using enhanced version above */

/* GET - Retrieve all path positions for current exercise */
router.get("/", function (req, res, next) {
  const PersonPathPosition = req.app.models.PersonPathPosition;
  const { exercise_id, limit, offset } = req.query;

  let whereCondition = {};
  
  if (exercise_id) {
    whereCondition.exercise_id = exercise_id;
  } else if (globalThis.exerciseId) {
    whereCondition.exercise_id = globalThis.exerciseId;
  }

  const queryOptions = {
    where: whereCondition,
    order: [['timestamp', 'ASC']],
    include: [
      {
        model: req.app.models.Devices,
        as: 'device',
        attributes: ['name', 'type']
      },
      {
        model: req.app.models.Maps,
        as: 'map',
        attributes: ['name']
      }
    ]
  };

  if (limit) {
    queryOptions.limit = parseInt(limit);
  }
  if (offset) {
    queryOptions.offset = parseInt(offset);
  }

  PersonPathPosition.findAll(queryOptions)
    .then((pathPositions) => {
      res.json({
        error: false,
        data: pathPositions,
        total: pathPositions.length
      });
    })
    .catch((error) => {
      console.error("Error retrieving path positions:", error);
      res.status(500).json({
        error: true,
        message: "Failed to retrieve path positions",
        details: error.message
      });
    });
});

/* GET - Calculate total distance traveled by a person using Haversine formula */
router.get("/distance/:serial_no", function (req, res, next) {
  const PersonPathPosition = req.app.models.PersonPathPosition;
  const { serial_no } = req.params;
  const { exercise_id, start_time, end_time } = req.query;

  // Build query options
  const whereClause = { serial_no: serial_no };
  if (exercise_id) {
    whereClause.exercise_id = exercise_id;
  }

  PersonPathPosition.findOne({
    where: whereClause,
    order: [['createdAt', 'DESC']]
  })
  .then((pathData) => {
    if (!pathData) {
      return res.status(404).json({
        error: true,
        message: `No path data found for person ${serial_no}`
      });
    }

    // Parse arrays
    const latitudes = pathData.latitude_array ? pathData.latitude_array.split(',').map(Number) : [];
    const longitudes = pathData.longitude_array ? pathData.longitude_array.split(',').map(Number) : [];
    const timestamps = pathData.timestamp_array ? pathData.timestamp_array.split(',') : [];

    // Validate arrays
    if (latitudes.length < 2 || longitudes.length < 2) {
      return res.json({
        error: false,
        serial_no: serial_no,
        total_distance_meters: 0,
        total_distance_km: 0,
        points_count: latitudes.length,
        message: "Insufficient points to calculate distance (need at least 2)"
      });
    }

    if (latitudes.length !== longitudes.length) {
      return res.status(400).json({
        error: true,
        message: "Latitude and longitude array lengths do not match"
      });
    }

    // Filter by time range if provided
    let filteredIndices = [];
    if (start_time && end_time) {
      const startDate = new Date(start_time);
      const endDate = new Date(end_time);
      
      for (let i = 0; i < timestamps.length; i++) {
        const pointDate = new Date(timestamps[i]);
        if (pointDate >= startDate && pointDate <= endDate) {
          filteredIndices.push(i);
        }
      }
    } else {
      // Use all points
      filteredIndices = Array.from({ length: latitudes.length }, (_, i) => i);
    }

    if (filteredIndices.length < 2) {
      return res.json({
        error: false,
        serial_no: serial_no,
        total_distance_meters: 0,
        total_distance_km: 0,
        points_count: filteredIndices.length,
        message: "Insufficient points in time range to calculate distance"
      });
    }

    // Calculate total distance using Haversine formula
    let totalDistance = 0;
    for (let i = 1; i < filteredIndices.length; i++) {
      const idx1 = filteredIndices[i - 1];
      const idx2 = filteredIndices[i];
      
      const distance = haversineDistance(
        latitudes[idx1], 
        longitudes[idx1],
        latitudes[idx2], 
        longitudes[idx2]
      );
      
      totalDistance += distance;
    }

    res.json({
      error: false,
      serial_no: serial_no,
      total_distance_meters: Math.round(totalDistance * 100) / 100, // Round to 2 decimals
      total_distance_km: Math.round((totalDistance / 1000) * 100) / 100,
      points_count: filteredIndices.length,
      exercise_id: pathData.exercise_id,
      map_id: pathData.map_id,
      time_range: start_time && end_time ? { start_time, end_time } : null
    });
  })
  .catch((error) => {
    console.error(`[PersonPathPositions] Error calculating distance for ${serial_no}:`, error);
    res.status(500).json({
      error: true,
      message: "Failed to calculate distance",
      details: error.message
    });
  });
});

/**
 * Haversine formula: Calculate great-circle distance between two lat/lng points
 * @param {number} lat1 - Latitude of point 1 (degrees)
 * @param {number} lon1 - Longitude of point 1 (degrees)
 * @param {number} lat2 - Latitude of point 2 (degrees)
 * @param {number} lon2 - Longitude of point 2 (degrees)
 * @returns {number} Distance in meters
 */
function haversineDistance(lat1, lon1, lat2, lon2) {
  const R = 6371000; // Earth radius in meters
  
  const dLat = (lat2 - lat1) * Math.PI / 180;
  const dLon = (lon2 - lon1) * Math.PI / 180;
  
  const a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
            Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
            Math.sin(dLon / 2) * Math.sin(dLon / 2);
  
  const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  
  return R * c; // Distance in meters
}

module.exports = router;