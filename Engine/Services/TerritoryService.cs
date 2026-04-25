using System;
using System.Collections.Generic;
using Engine.Models;
using System.Linq;

namespace Engine.Services;

/// <summary>
/// Contiene las referencias exactas (físicas) de lo que Godot ha seleccionado en el mapa.
/// </summary>
public class TransferPayload
{
    public Province Source { get; set; } = null!;
    
    /// <summary>
    /// Provincia destino. Si es null, se creará una nueva provincia.
    /// </summary>
    public Province? Target { get; set; }
    
    /// <summary>
    /// Si Target es null, estos datos se usarán para crear la nueva provincia.
    /// </summary>
    public string NewProvinceId { get; set; } = "";
    public string NewProvinceNameKey { get; set; } = "";
    public Country? NewOwner { get; set; }
    
    // Lo que Godot detectó físicamente dentro del área pintada:
    public List<EmploymentSlot> SlotsToMove { get; set; } = new();
    public List<PopGroup> PopsToMove { get; set; } = new();
    
    /// <summary>
    /// Ratio arbitrario (0.0 a 1.0) para transferir los recursos del mercado provincial.
    /// Decidido por Godot (ej. si el Almacén central entra en la selección, sería 1.0f).
    /// </summary>
    public float MarketTransferRatio { get; set; } = 0f; 
}

/// <summary>
/// Servicio para gestionar el redibujado de territorios basado en selecciones físicas (City-Builder).
/// </summary>
public class TerritoryService
{
    private readonly GameContext _ctx;

    public TerritoryService(GameContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>
    /// Ejecuta una transferencia explícita de elementos físicos (Pops, Edificios, % Mercado)
    /// de una provincia origen a una destino (o una nueva).
    /// </summary>
    public Province? ExecuteTransfer(TransferPayload payload)
    {
        if (payload.Source == null) return null;

        Province? target = payload.Target;
        bool isNewProvince = false;

        // 1. Crear nueva provincia si no hay Target
        if (target == null)
        {
            target = new Province(payload.NewProvinceId, payload.NewProvinceNameKey, payload.Source.RegionKey)
            {
                Owner = payload.NewOwner ?? payload.Source.Owner
            };
            isNewProvince = true;
        }

        // 2. Transferir Empleos / Edificios
        foreach (var slot in payload.SlotsToMove)
        {
            if (payload.Source.EmploymentSlots.Contains(slot))
            {
                payload.Source.EmploymentSlots.Remove(slot);
                
                // En un city builder los edificios son instancias físicas.
                // Simplemente los añadimos a la nueva provincia.
                target.EmploymentSlots.Add(slot);
            }
        }

        // 3. Transferir Pops (Personas / Residencias)
        foreach (var pop in payload.PopsToMove)
        {
            if (payload.Source.Pops.Contains(pop))
            {
                payload.Source.Pops.Remove(pop);

                // Si la fábrica del Pop NO fue transferida, el pop debe perder su empleo
                // (Se separan geográficamente, rompemos el vínculo estricto del motor base).
                if (pop.CurrentEmployment != null && !payload.SlotsToMove.Contains(pop.CurrentEmployment))
                {
                    pop.CurrentEmployment.AssignedCount -= pop.EmployedCount;
                    if (pop.CurrentEmployment.AssignedCount < 0) pop.CurrentEmployment.AssignedCount = 0;
                    
                    // Si fuimos los únicos, limpiamos el slot
                    if (pop.CurrentEmployment.AssignedCount == 0)
                        pop.CurrentEmployment.AssignedPop = null;
                        
                    pop.CurrentEmployment = null;
                    pop.EmployedCount = 0;
                }
                
                // Mantenemos la instancia original para que Godot no pierda el puntero a su PopNode 3D/2D
                target.Pops.Add(pop);
            }
        }
        
        // Limpieza de vínculos rotos inversa:
        // (Fábricas que se movieron pero sus trabajadores se quedaron atrás)
        foreach (var slot in payload.SlotsToMove)
        {
            if (slot.AssignedPop != null && !payload.PopsToMove.Contains(slot.AssignedPop))
            {
                // El trabajador se quedó en Source
                slot.AssignedPop.CurrentEmployment = null;
                slot.AssignedPop.EmployedCount = 0;
                
                slot.AssignedPop = null;
                slot.AssignedCount = 0;
            }
        }

        // 4. Transferir Economía (Mercado abstracto o Almacenes)
        if (payload.MarketTransferRatio > 0)
        {
            float ratio = Math.Clamp(payload.MarketTransferRatio, 0f, 1f);
            var extractedMarket = payload.Source.Market.Split(ratio);
            target.Market.Merge(extractedMarket);
        }

        // 5. Registrar si es nueva
        if (isNewProvince)
        {
            _ctx.Provinces.Add(target);
            if (target.Owner != null)
            {
                target.Owner.Provinces.Add(target);
            }
        }

        return target;
    }

    /// <summary>
    /// Cambia íntegramente la titularidad de una provincia entera de un país a otro.
    /// (Equivale a una anexión completa de la región administrativa).
    /// </summary>
    public void TransferProvinceOwnership(Province province, Country newOwner)
    {
        if (province.Owner != null)
        {
            province.Owner.Provinces.Remove(province);
        }
        
        province.Owner = newOwner;
        
        if (newOwner != null && !newOwner.Provinces.Contains(province))
        {
            newOwner.Provinces.Add(province);
        }
    }
}
