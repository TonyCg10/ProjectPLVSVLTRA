Game.Log("El mod The Spice Route se ha inicializado correctamente en Lua.")

-- Suscribirse al hook de producción para inyectar especias al mercado mágicamente
Game.Subscribe("AfterProductionPhase", function(province)
    -- Añadir 5 unidades de especias gratis a la provincia en cada tick
    province.Market.AddSupply("spices", 5.0)
    
    -- Hacer log solo el día 1 del año para no saturar la consola (ejemplo de control de flujo)
    -- Lamentablemente, desde aquí no tenemos el tick si no lo pasamos en el hook, 
    -- pero para la prueba, esto simplemente correrá e inyectará especias silenciasamente.
end)

Game.Subscribe("OnYearEnd", function(context)
    Game.Log("¡Un nuevo año de comercio de especias comienza!")
end)
