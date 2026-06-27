window.getGeolocation = () => new Promise((resolve, reject) => {
    if (!navigator.geolocation)
        return reject("Géolocalisation non supportée par ce navigateur.");
    navigator.geolocation.getCurrentPosition(
        p => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude }),
        e => reject(e.message),
        { timeout: 10000 }
    );
});
